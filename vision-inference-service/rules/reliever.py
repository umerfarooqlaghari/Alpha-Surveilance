"""
rules/reliever.py

Factory Worker Reliever System Evaluator.

Monitors factory workstation polygons, manages worker handover lifecycle:
1. Normal State: Required primary workers present inside the workstation polygon.
2. Pending Handover State: Primary worker leaves the polygon; a grace threshold timer starts.
3. Active Relief State: Reliever enters the polygon before the grace threshold expires; tracks relief duration.
4. Unattended Workstation Violation: Grace threshold expires with no reliever stationed.
5. Overdue Relief Alert: Reliever exceeds maximum allowed relief duration.

Features:
- Dynamic capacities: number of primary workers, number of relievers.
- Dynamic thresholds: handover grace period (seconds), max relief duration (seconds).
- Normalized [0..1] and pixel coordinate polygon support.
- Thread-safe module store keyed by (camera_id, rule_hash).
- Hot-reload resilient.
- Asynchronous operational event queue for real-time live board syncing.
"""

import hashlib
import json
import logging
import threading
import time
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional, Set, Tuple

from shapely.geometry import Point

from rules.spatial import (
    _anchor_point,
    _get_or_build_polygon,
    _log_once,
    _ALLOWED_ANCHORS,
    _ALLOWED_SPACES,
)

logger = logging.getLogger("vision-service.rules.reliever")

_DEFAULT_HANDOVER_THRESHOLD_S = 60.0
_DEFAULT_MAX_RELIEF_DURATION_S = 900.0
_DEFAULT_REQUIRED_PRIMARIES = 1

# ── Module-level state store ──────────────────────────────────────────────────
# Key: "{camera_id}:{rule_hash}"
_RELIEVER_STORE: Dict[str, Dict] = {}
_RELIEVER_MUTEX: threading.Lock = threading.Lock()

# ── Thread-safe operational relief event queue ────────────────────────────────
_PENDING_RELIEF_EVENTS: List[Dict] = []
_EVENTS_MUTEX: threading.Lock = threading.Lock()


def enqueue_relief_event(event: Dict) -> None:
    with _EVENTS_MUTEX:
        _PENDING_RELIEF_EVENTS.append(event)


def pop_pending_relief_events() -> List[Dict]:
    with _EVENTS_MUTEX:
        if not _PENDING_RELIEF_EVENTS:
            return []
        events = list(_PENDING_RELIEF_EVENTS)
        _PENDING_RELIEF_EVENTS.clear()
        return events


def _extract_rule_config(rule: Any) -> Dict:
    """Extract rule_config dictionary whether rule is a dataclass or a dict."""
    if isinstance(rule, dict):
        return rule.get("rule_config") if "rule_config" in rule else rule
    return getattr(rule, "rule_config", {}) or {}


def _extract_rule_attr(rule: Any, attr: str, default: Any = None) -> Any:
    """Extract an attribute from a rule object or dictionary."""
    if isinstance(rule, dict):
        return rule.get(attr, default)
    return getattr(rule, attr, default)


def _rule_hash(rule_config: Dict) -> str:
    """Stable fingerprint of the workstation polygon and timing config."""
    payload = {
        "polygon": rule_config.get("polygon", []),
        "coordinate_space": rule_config.get("coordinate_space", "pixel"),
        "handover_threshold_s": rule_config.get("handover_threshold_s", rule_config.get("threshold_s", _DEFAULT_HANDOVER_THRESHOLD_S)),
        "max_relief_duration_s": rule_config.get("max_relief_duration_s", _DEFAULT_MAX_RELIEF_DURATION_S),
        "required_primaries": rule_config.get("required_primaries", _DEFAULT_REQUIRED_PRIMARIES),
        "operating_schedules": rule_config.get("operating_schedules"),
    }
    raw = json.dumps(payload, sort_keys=True)
    return hashlib.sha256(raw.encode("utf-8")).hexdigest()[:16]


def get_active_operating_shift(rule_config: Dict, now_dt: Optional[datetime] = None) -> Optional[Dict]:
    """
    Returns the currently active operating shift window dict, or None if outside all windows.
    If no operating schedule is configured, returns None (indicating 24/7 continuous operation).
    """
    schedules = rule_config.get("operating_schedules") or rule_config.get("operating_schedule")
    if not schedules:
        return None

    if isinstance(schedules, str):
        try:
            schedules = json.loads(schedules)
        except Exception:
            return None

    if not isinstance(schedules, list) or len(schedules) == 0:
        return None

    active_windows = [w for w in schedules if isinstance(w, dict) and w.get("isActive", True)]
    if not active_windows:
        return None

    dt = now_dt or datetime.now(timezone.utc)
    # Day of week: 0=Sunday, 1=Monday, ..., 6=Saturday
    # Python weekday(): Monday=0, Sunday=6 -> (dt.weekday() + 1) % 7
    dow = (dt.weekday() + 1) % 7
    cur_minutes = dt.hour * 60 + dt.minute

    for win in active_windows:
        start_str = win.get("startTime", "")
        end_str = win.get("endTime", "")
        if not start_str or not end_str:
            continue

        try:
            sh, sm = [int(p) for p in start_str.split(":")]
            eh, em = [int(p) for p in end_str.split(":")]
            s_min = sh * 60 + sm
            e_min = eh * 60 + em
        except Exception:
            continue

        if e_min == 0 and s_min > 0:
            e_min = 1440

        days = win.get("daysOfWeek")
        if not days:
            days = list(range(7))
        else:
            days = [int(d) for d in days if isinstance(d, (int, float))]

        if s_min < e_min:
            # Daytime window on same day [s_min, e_min)
            if dow in days and s_min <= cur_minutes < e_min:
                return win
        elif s_min > e_min:
            # Overnight window spanning midnight, e.g. 22:00 to 06:00
            # Evening portion (start day):
            if dow in days and cur_minutes >= s_min:
                return win
            # Morning portion (day after start day):
            prev_dow = (dow - 1) % 7
            if prev_dow in days and cur_minutes < e_min:
                return win

    return None


def is_within_operating_hours(rule_config: Dict, now_dt: Optional[datetime] = None) -> bool:
    """
    Checks whether current UTC time falls within any active operating timing window.
    If no operating schedule is configured, returns True (operates 24/7).
    """
    schedules = rule_config.get("operating_schedules") or rule_config.get("operating_schedule")
    if not schedules:
        return True

    if isinstance(schedules, str):
        try:
            schedules = json.loads(schedules)
        except Exception:
            return True

    if not isinstance(schedules, list) or len(schedules) == 0:
        return True

    active_windows = [w for w in schedules if isinstance(w, dict) and w.get("isActive", True)]
    if not active_windows:
        return True

    return get_active_operating_shift(rule_config, now_dt) is not None


def _get_or_init_state(camera_id: str, rule_config: Dict) -> Dict:
    r_hash = _rule_hash(rule_config)
    store_key = f"{camera_id}:{r_hash}"

    with _RELIEVER_MUTEX:
        if store_key not in _RELIEVER_STORE:
            _RELIEVER_STORE[store_key] = {
                "lock": threading.Lock(),
                "state": "NORMAL",
                "primary_left_at": 0.0,
                "reliever_arrived_at": None,
                "active_reliever_id": None,
                "violation_emitted": False,
                "overdue_emitted": False,
                "last_seen_primary_tracks": set(),
                "last_seen_reliever_tracks": set(),
                "last_frame_ts": time.time(),
            }
        return _RELIEVER_STORE[store_key]


def _is_reliever_person(det: Dict, rule_config: Dict, active_shift: Optional[Dict] = None) -> bool:
    """
    Checks if a detected person is identified as a Reliever.
    Looks at Re-ID identity info, person tags, roles, or assigned reliever IDs
    (prioritizing active shift-specific reliever roster if configured).
    """
    identity = det.get("identity_info") or {}
    employee_id = str(identity.get("employeeId") or identity.get("person_id") or "").strip().lower()
    role = str(identity.get("role") or identity.get("designation") or "").strip().lower()
    person_tag = str(det.get("person_tag") or "").strip().lower()

    if "reliever" in role or "reliever" in person_tag:
        return True

    # If the active shift defines a dedicated reliever roster, check that first
    shift_relievers = None
    if active_shift:
        shift_relievers = active_shift.get("reliever_employee_ids") or active_shift.get("relieverEmployeeIds")

    allowed_relievers = shift_relievers if (shift_relievers is not None and len(shift_relievers) > 0) else (
        rule_config.get("reliever_employee_ids") or rule_config.get("allowed_reliever_ids") or []
    )
    if isinstance(allowed_relievers, (list, set, tuple)):
        allowed_set = {str(x).strip().lower() for x in allowed_relievers}
        if employee_id and employee_id in allowed_set:
            return True

    return False


def evaluate_reliever_rule(
    det: Dict,
    rule_config: Dict,
    frame_size: Optional[Tuple[int, int]] = None,
    camera_id: str = "",
) -> Tuple[bool, Optional[str], Optional[Dict]]:
    """
    Evaluates human detections against the workstation polygon.

    Returns:
      (is_violation, violation_label, extra_metadata)
    """
    polygon_coords = rule_config.get("polygon")
    coord_space = (rule_config.get("coordinate_space") or "pixel").lower()
    anchor_name = (rule_config.get("anchor") or "bottom_center").lower()

    poly = _get_or_build_polygon(rule_config, polygon_coords, coord_space, frame_size)
    if not poly:
        return False, None, None

    px, py = _anchor_point(det["box"], anchor_name)
    pt = Point(px, py)
    is_inside = poly.contains(pt)

    if not is_inside:
        return False, None, None

    # Off-duty suppression: do not track handovers or emit overdue alerts outside operating hours
    if not is_within_operating_hours(rule_config):
        return False, None, None

    # Person is inside the workstation polygon
    active_shift = get_active_operating_shift(rule_config)
    track_id = str(det.get("track_id") or f"{round(px, -1)}_{round(py, -1)}")
    is_reliever = _is_reliever_person(det, rule_config, active_shift=active_shift)
    identity = det.get("identity_info") or {}
    emp_external_id = str(identity.get("employeeId") or identity.get("person_id") or "").strip()

    max_relief_duration_s = float(rule_config.get("max_relief_duration_s", _DEFAULT_MAX_RELIEF_DURATION_S))
    ws_id = rule_config.get("workstation_id") or "00000000-0000-0000-0000-000000000000"
    tenant_id = rule_config.get("tenant_id") or "00000000-0000-0000-0000-000000000000"

    state_obj = _get_or_init_state(camera_id, rule_config)
    now = time.time()

    with state_obj["lock"]:
        state_obj["last_frame_ts"] = now

        if is_reliever:
            state_obj["last_seen_reliever_tracks"].add(track_id)
            if state_obj["state"] in ("NORMAL", "PENDING_HANDOVER", "UNATTENDED_VIOLATION"):
                was_pending = state_obj["state"] in ("PENDING_HANDOVER", "UNATTENDED_VIOLATION")
                state_obj["state"] = "ACTIVE_RELIEF"
                if state_obj["reliever_arrived_at"] is None:
                    state_obj["reliever_arrived_at"] = now
                state_obj["active_reliever_id"] = track_id
                state_obj["violation_emitted"] = False

                if was_pending:
                    logger.info("Workstation on cam %s entered ACTIVE_RELIEF with reliever %s", camera_id, track_id)
                    enqueue_relief_event({
                        "TenantId": tenant_id,
                        "WorkstationId": ws_id,
                        "CameraId": camera_id,
                        "EventType": "RELIEVER_ENTER",
                        "EmployeeExternalId": emp_external_id,
                        "Role": "Reliever",
                        "Timestamp": datetime.now(timezone.utc).isoformat(),
                        "Notes": f"Reliever {emp_external_id or track_id} arrived at workstation",
                    })

            elif state_obj["state"] == "ACTIVE_RELIEF":
                # Check for overdue relief duration
                if state_obj["reliever_arrived_at"] is not None:
                    duration = now - state_obj["reliever_arrived_at"]
                    if duration >= max_relief_duration_s and not state_obj["overdue_emitted"]:
                        state_obj["state"] = "OVERDUE_ALERT"
                        state_obj["overdue_emitted"] = True
                        logger.warning(
                            "Workstation on cam %s relief overdue: %.1fs > %.1fs",
                            camera_id, duration, max_relief_duration_s
                        )
                        enqueue_relief_event({
                            "TenantId": tenant_id,
                            "WorkstationId": ws_id,
                            "CameraId": camera_id,
                            "EventType": "OVERDUE_TIMEOUT",
                            "EmployeeExternalId": emp_external_id,
                            "Role": "Reliever",
                            "Timestamp": datetime.now(timezone.utc).isoformat(),
                            "Notes": f"Relief duration exceeded max limit of {max_relief_duration_s}s",
                        })
                        return True, "overdue_relief_duration", {
                            "workstation_state": "OVERDUE_ALERT",
                            "relief_duration_s": round(duration, 1),
                            "max_allowed_s": max_relief_duration_s,
                            "reliever_track_id": track_id
                        }
        else:
            # Primary worker present
            state_obj["last_seen_primary_tracks"].add(track_id)
            if state_obj["state"] != "NORMAL":
                # Primary worker returned
                state_obj["state"] = "NORMAL"
                state_obj["primary_left_at"] = 0.0
                state_obj["reliever_arrived_at"] = None
                state_obj["active_reliever_id"] = None
                state_obj["violation_emitted"] = False
                state_obj["overdue_emitted"] = False
                logger.info("Workstation on cam %s returned to NORMAL state", camera_id)

                enqueue_relief_event({
                    "TenantId": tenant_id,
                    "WorkstationId": ws_id,
                    "CameraId": camera_id,
                    "EventType": "PRIMARY_RETURN",
                    "EmployeeExternalId": emp_external_id,
                    "Role": "Primary",
                    "Timestamp": datetime.now(timezone.utc).isoformat(),
                    "Notes": f"Primary operator {emp_external_id or track_id} returned to workstation",
                })

    return False, None, None


def check_unattended_workstations(camera_id: str, configured_rules: List[Any]) -> List[Dict]:
    """
    Called at end of frame processing to detect if any workstation
    has zero occupants and has exceeded the handover grace threshold.
    """
    violations = []
    now = time.time()

    for rule in configured_rules:
        rule_config = _extract_rule_config(rule)
        rule_type = str(rule_config.get("type") or "").lower()
        if rule_type not in ("reliever", "workstation_relief"):
            continue

        state_obj = _get_or_init_state(camera_id, rule_config)
        threshold_s = float(rule_config.get("handover_threshold_s", rule_config.get("threshold_s", _DEFAULT_HANDOVER_THRESHOLD_S)))
        ws_id = rule_config.get("workstation_id") or "00000000-0000-0000-0000-000000000000"
        tenant_id = rule_config.get("tenant_id") or "00000000-0000-0000-0000-000000000000"
        rule_name = _extract_rule_attr(rule, "name", "Workstation Relief Rule")
        model_id = _extract_rule_attr(rule, "model_identifier", "workstation-reliever")
        sop_id = _extract_rule_attr(rule, "sop_violation_type_id")

        # Suppress unattended alarms during off-duty hours
        is_on_duty = is_within_operating_hours(rule_config, datetime.now(timezone.utc))
        if not is_on_duty:
            with state_obj["lock"]:
                state_obj["state"] = "OFF_DUTY"
                state_obj["primary_left_at"] = 0.0
                state_obj["violation_emitted"] = False
                state_obj["last_seen_primary_tracks"].clear()
                state_obj["last_seen_reliever_tracks"].clear()
            continue

        with state_obj["lock"]:
            has_primary = len(state_obj["last_seen_primary_tracks"]) > 0
            has_reliever = len(state_obj["last_seen_reliever_tracks"]) > 0

            # Clear per-frame presence sets for next frame
            state_obj["last_seen_primary_tracks"].clear()
            state_obj["last_seen_reliever_tracks"].clear()

            if has_primary:
                state_obj["state"] = "NORMAL"
                state_obj["primary_left_at"] = 0.0
                state_obj["violation_emitted"] = False
            elif has_reliever:
                if state_obj["state"] not in ("ACTIVE_RELIEF", "OVERDUE_ALERT"):
                    state_obj["state"] = "ACTIVE_RELIEF"
                    state_obj["reliever_arrived_at"] = now
            else:
                # Nobody present in the workstation polygon
                if state_obj["state"] in ("NORMAL", "OFF_DUTY"):
                    # Transition to PENDING_HANDOVER
                    state_obj["state"] = "PENDING_HANDOVER"
                    state_obj["primary_left_at"] = now
                    state_obj["violation_emitted"] = False
                    logger.info("Workstation on cam %s: Primary absent during shift, started grace timer (%.1fs)", camera_id, threshold_s)

                    enqueue_relief_event({
                        "TenantId": tenant_id,
                        "WorkstationId": ws_id,
                        "CameraId": camera_id,
                        "EventType": "PRIMARY_EXIT",
                        "Role": "Primary",
                        "Timestamp": datetime.now(timezone.utc).isoformat(),
                        "Notes": "Primary operator exited workstation; grace period started",
                    })

                elif state_obj["state"] == "PENDING_HANDOVER":
                    elapsed = now - state_obj["primary_left_at"]
                    if elapsed >= threshold_s and not state_obj["violation_emitted"]:
                        state_obj["state"] = "UNATTENDED_VIOLATION"
                        state_obj["violation_emitted"] = True
                        logger.warning(
                            "Workstation on cam %s UNATTENDED violation triggered: elapsed %.1fs >= threshold %.1fs",
                            camera_id, elapsed, threshold_s
                        )

                        enqueue_relief_event({
                            "TenantId": tenant_id,
                            "WorkstationId": ws_id,
                            "CameraId": camera_id,
                            "EventType": "UNATTENDED_TIMEOUT",
                            "Role": "Primary",
                            "Timestamp": datetime.now(timezone.utc).isoformat(),
                            "Notes": f"Grace period of {threshold_s}s expired with no reliever stationed",
                        })

                        viol = {
                            "violation_type": "unattended_workstation",
                            "label": "unattended_workstation",
                            "camera_id": camera_id,
                            "rule_name": rule_name,
                            "matched_rule": rule_name,
                            "source_model": model_id,
                            "elapsed_unattended_s": round(elapsed, 1),
                            "threshold_s": threshold_s,
                            "score": 0.99,
                            "box": {"xmin": 0, "ymin": 0, "xmax": 0, "ymax": 0},
                        }
                        if sop_id:
                            viol["sop_violation_type_id"] = sop_id
                        violations.append(viol)

    return violations
