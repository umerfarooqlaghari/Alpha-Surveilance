"""
rules/dwell.py

Dwell-time policy filter.

A dwell rule fires only when a tracked subject **stays continuously inside a
zone** for at least ``duration_s`` seconds. This dramatically reduces false-
positives from people walking through a restricted area for half a second —
the most common complaint about a pure-geofence rule.

Behavior contract used by ``rules.evaluator``:
  - Return True  -> dwell threshold reached -> detection PASSES.
  - Return False -> still accumulating (or outside zone) -> SUPPRESSED.

Schema (post server-side validation):
  type:              "dwell"  (required)
  polygon:           [[x, y], ...]  3..MAX_POLYGON_VERTICES vertices
  coordinate_space:  "pixel" (default) | "normalized"
  mode:              "entry" (default) | "exit"
  anchor:            "bottom_center" (default) | "centroid" | "top_center"
  duration_s:        positive number in (0, 3600]

Stateful design:
  - Per-track state lives in a **module-level store** (``_DWELL_STORE``), keyed
    by ``"{camera_id}:{rule_content_hash}"`` where the hash covers the fields
    that define the zone's shape and timing: polygon, mode, anchor, duration_s.
    This means the dwell timer SURVIVES hot-reloads: if an operator updates an
    unrelated camera field (name, FPS, RTSP URL), the store entry is reused and
    no in-progress dwell timer is lost.  Only a change to the zone geometry or
    timing resets the timer (correct behaviour).
    The old design stored state on the rule_config dict itself; because
    ``ViolationApiClient.fetch_active_cameras()`` builds fresh dicts on every
    reload, any live dwell accumulation was silently wiped (C-1 fix).
  - Each entry is a ``(state_dict, threading.Lock)`` pair.
    ``state_dict`` maps track-key → ``(entered_at, last_seen_at)`` tuples:
      * ``entered_at``   — when the subject first entered the violating zone.
                           Never updated while they remain inside.
      * ``last_seen_at`` — wall-clock of the most recent frame where the subject
                           was still in the violating state. Updated every frame.
    The GC compares ``last_seen_at`` so continuously-present subjects are never
    evicted (Bug #1 fix).
  - The detection must carry a ``track_id`` (injected by ``SimpleIouTracker``
    in ``rtsp/violation_manager``). If track_id is missing, the rule falls
    back to a quantized centroid-bucket pseudo-id so a stationary subject
    can still accumulate dwell time — this is intentionally permissive.
  - The per-entry ``threading.Lock`` serialises state mutations so concurrent
    frames from different threads are safe (Issue #7 fix preserved).
"""
import hashlib
import json
import logging
import threading
import time
from typing import Dict, Optional, Tuple

from shapely.geometry import Point

from rules.spatial import (
    _anchor_point,
    _get_or_build_polygon,
    _log_once,
    _ALLOWED_ANCHORS,
    _ALLOWED_MODES,
    _ALLOWED_SPACES,
)

logger = logging.getLogger("vision-service.rules.dwell")

_DEFAULT_DURATION_S = 5.0
_MAX_DURATION_S = 3600.0

# ── C-1 fix: module-level dwell state store ───────────────────────────────────
# Key: "{camera_id}:{rule_content_hash}" — stable across hot-reloads because it
# derives from rule *content* (polygon/mode/anchor/duration_s), not dict identity.
# Value: (state_dict, per-entry threading.Lock).
# state_dict maps track-key → (entered_at, last_seen_at).
_DWELL_STORE: Dict[str, Tuple[Dict[str, Tuple[float, float]], threading.Lock]] = {}
_DWELL_STORE_MUTEX: threading.Lock = threading.Lock()

# I-3 fix: per-(camera, rule) initialization timestamp.  When an exit-mode
# dwell rule is first armed, we cannot tell whether a currently-outside subject
# just stepped out (legitimate alert imminent) or has been outside for hours
# (false alert).  We suppress exit-mode alerts for ``duration_s`` seconds after
# initialization so the operator gets a clean baseline before any alert can
# fire.  After the grace period, normal dwell semantics resume.
_DWELL_INIT_TIMES: Dict[str, float] = {}


def _rule_content_key(rule_config: Dict) -> str:
    """16-char hex hash of the shape-defining fields of a dwell rule.

    Only polygon, mode, anchor, and duration_s are included. Changes to these
    fields reset the dwell timer (the zone was redrawn/adjusted). Unrelated
    fields (coordinate_space, camera name, FPS) are excluded so a hot-reload
    that doesn't touch the zone geometry doesn't wipe in-progress timers.
    """
    relevant = {
        "polygon": rule_config.get("polygon"),
        "mode": rule_config.get("mode"),
        "anchor": rule_config.get("anchor"),
        "duration_s": rule_config.get("duration_s"),
    }
    blob = json.dumps(relevant, sort_keys=True, default=str).encode()
    return hashlib.sha256(blob).hexdigest()[:16]


def _get_dwell_store_entry(
    camera_id: str,
    rule_config: Dict,
) -> Tuple[Dict[str, Tuple[float, float]], threading.Lock]:
    """Return the ``(state_dict, lock)`` for this ``(camera_id, rule)`` pair.

    Uses double-checked locking on ``_DWELL_STORE_MUTEX`` so two threads that
    simultaneously encounter a new camera/rule can't both create separate entries.
    """
    store_key = f"{camera_id}:{_rule_content_key(rule_config)}"
    entry = _DWELL_STORE.get(store_key)
    if entry is None:
        with _DWELL_STORE_MUTEX:
            entry = _DWELL_STORE.get(store_key)
            if entry is None:
                entry = ({}, threading.Lock())
                _DWELL_STORE[store_key] = entry
    return entry


def get_dwell_state(camera_id: str, rule_config: Dict) -> Dict[str, Tuple[float, float]]:
    """Return a snapshot of the current dwell state for a (camera_id, rule) pair.

    Returns an empty dict when no state exists yet.  This is a read-only helper
    intended for tests and diagnostics — do not mutate the returned dict.
    """
    store_key = f"{camera_id}:{_rule_content_key(rule_config)}"
    entry = _DWELL_STORE.get(store_key)
    return dict(entry[0]) if entry else {}


def _track_key(detection: Dict, anchor_x: float, anchor_y: float) -> str:
    """
    Stable identifier for a single subject across frames.

    Prefers ``track_id`` from the IoU tracker. When unavailable, falls back
    to a quantized centroid bucket — coarse enough to absorb small jitter
    (~20px buckets) so a near-stationary subject keeps the same key.
    """
    tid = detection.get("track_id")
    if tid is not None:
        return f"t:{tid}"
    bx = int(anchor_x // 20)
    by = int(anchor_y // 20)
    label = str(detection.get("label", "")).lower()
    return f"b:{label}:{bx}:{by}"


def _gc_expired(state: Dict[str, Tuple[float, float]], duration_s: float, now: float) -> None:
    """Drop tracks whose ``last_seen_at`` is older than ``duration_s`` seconds ago.

    Uses the second element of the ``(entered_at, last_seen_at)`` tuple so
    that continuously present subjects (who never update ``entered_at``) are
    never evicted while they remain inside the zone.
    """
    if not state:
        return
    expiry_window = max(duration_s, _DEFAULT_DURATION_S)
    stale = [k for k, (_, last_seen) in state.items() if last_seen < now - expiry_window]
    for k in stale:
        state.pop(k, None)


def evaluate_dwell_rule(
    detection: Dict,
    rule_config: Dict,
    frame_size: Optional[Tuple[int, int]] = None,
    now: Optional[float] = None,
    camera_id: str = "",
) -> bool:
    rule_type = (rule_config.get("type") or "").lower()
    if rule_type != "dwell":
        return True  # not our rule type — let dispatcher decide

    polygon_coords = rule_config.get("polygon")
    coord_space = (rule_config.get("coordinate_space") or "pixel").lower()
    mode = (rule_config.get("mode") or "entry").lower()
    anchor = (rule_config.get("anchor") or "bottom_center").lower()

    if coord_space not in _ALLOWED_SPACES:
        _log_once(rule_config, "dwell-bad-coord-space",
                  "Dwell rule has unsupported coordinate_space=%r; suppressing.", coord_space)
        return False
    if mode not in _ALLOWED_MODES:
        _log_once(rule_config, "dwell-bad-mode",
                  "Dwell rule has unsupported mode=%r; suppressing.", mode)
        return False
    if anchor not in _ALLOWED_ANCHORS:
        _log_once(rule_config, "dwell-bad-anchor",
                  "Dwell rule has unsupported anchor=%r; suppressing.", anchor)
        return False

    raw_duration = rule_config.get("duration_s", _DEFAULT_DURATION_S)
    try:
        duration_s = float(raw_duration)
    except (TypeError, ValueError):
        _log_once(rule_config, "dwell-bad-duration-type",
                  "Dwell rule has non-numeric duration_s=%r; suppressing.", raw_duration)
        return False
    if not (0.0 < duration_s <= _MAX_DURATION_S):
        _log_once(rule_config, "dwell-bad-duration-range",
                  "Dwell rule duration_s=%r outside (0, %d]; suppressing.",
                  raw_duration, int(_MAX_DURATION_S))
        return False

    poly = _get_or_build_polygon(rule_config, polygon_coords, coord_space, frame_size)
    if not poly:
        # _build_polygon already logged the specific failure tag once.
        return False

    try:
        x, y = _anchor_point(detection["box"], anchor)
    except (KeyError, TypeError):
        _log_once(rule_config, "dwell-missing-box",
                  "Detection missing 'box'; suppressing dwell rule.")
        return False

    inside = poly.intersects(Point(x, y))
    # In a "permitted" zone (mode=exit), the violating state is OUTSIDE.
    in_violating_state = inside if mode == "entry" else (not inside)

    # Resolve monotonic clock before taking the lock (it's a syscall; no need
    # to hold the lock while doing it).
    if now is None:
        now = time.monotonic()

    # C-1 fix: state lives in a module-level store, not on rule_config, so it
    # survives hot-reloads.  The per-entry lock still serialises concurrent
    # frames for the same camera/rule (Issue #7 invariant preserved).
    state, lock = _get_dwell_store_entry(camera_id, rule_config)
    store_key = f"{camera_id}:{_rule_content_key(rule_config)}"

    with lock:
        # I-3 fix: lazily record the first ``now`` we observe for this
        # (camera, rule).  We use the caller-provided ``now`` (not
        # ``time.monotonic()``) so tests with a synthetic clock work, and so
        # the grace window is anchored to the same time domain as ``entered_at``.
        init_at = _DWELL_INIT_TIMES.setdefault(store_key, now)

        key = _track_key(detection, x, y)

        if not in_violating_state:
            # Subject left the violating zone — reset the timer immediately so a
            # re-entry has to accumulate fresh dwell time.
            state.pop(key, None)
            _gc_expired(state, duration_s, now)
            return False

        entry = state.get(key)
        if entry is None:
            # First frame inside the zone: arm the timer.
            state[key] = (now, now)  # (entered_at, last_seen_at)
            _gc_expired(state, duration_s, now)
            return False

        # Subject is still inside: update last_seen_at so the GC never evicts
        # an actively dwelling subject (Bug #1 fix).
        entered_at, _ = entry
        state[key] = (entered_at, now)

        # Periodically drop stale tracks so the state dict doesn't grow
        # unboundedly over a long-running camera session.
        _gc_expired(state, duration_s, now)

        # I-3 fix: in exit-mode, suppress alerts during the startup grace
        # window.  Without this, a subject who has legitimately been outside
        # the zone for hours triggers an alert ``duration_s`` seconds after
        # the camera starts streaming.  We require BOTH the per-track
        # ``entered_at`` to be older than the rule init time AND the
        # grace window to have elapsed — i.e., the subject was observed
        # outside the zone before the rule armed, so we don't trust the
        # accumulated time.  Once init_at + duration_s has passed, any new
        # exit event seen after init_at trusts its own ``entered_at``.
        if mode == "exit" and entered_at <= init_at and (now - init_at) < duration_s:
            return False

        return (now - entered_at) >= duration_s
