"""
rules/spatial.py

Spatial (geofence / zone) policy filter.

Behavior contract used by ``rules.evaluator``:
  - Return True  -> detection PASSES the spatial filter (it may still be reported as a violation).
  - Return False -> detection is SUPPRESSED by the spatial filter.

Fail-closed semantics:
  - Malformed / unsafe polygon definitions return ``False`` (suppress) rather than ``True``
    (alert on everything). This avoids the previous fail-open behavior where a typo
    in the rule_config turned a camera into "alert on every detection".

Hot-path optimizations:
  - The compiled Shapely ``Polygon`` is cached on the rule_config dict itself,
    keyed by frame_size. A 30 FPS camera with 10 detections/frame would otherwise
    rebuild + ``make_valid`` the same polygon ~300 times/second.
  - Validation errors are logged exactly once per rule_config (sentinel flag in
    the dict) instead of every frame, preventing log spam from a misconfigured
    camera.
  - ``poly.intersects(point)`` is used instead of ``poly.contains(point)`` so
    that detections whose anchor falls exactly on the polygon boundary are
    treated as INSIDE — desirable since pixel coordinates are integer-quantized
    and boundary hits are common.
"""
import logging
from typing import Dict, List, Optional, Tuple

from shapely.geometry import Point, Polygon
from shapely.validation import make_valid

logger = logging.getLogger("vision-service.rules.spatial")

# Mirrors the server-side cap in RuleConfigurationValidator.cs (defense in depth).
MAX_POLYGON_VERTICES = 64

_ALLOWED_ANCHORS = {"centroid", "bottom_center", "top_center"}
_ALLOWED_MODES = {"entry", "exit"}
_ALLOWED_SPACES = {"pixel", "normalized"}

# Sentinel stored inside rule_config for log-once error suppression only.
# (Not state — just tracks which error tags have already been logged.)
_LOGGED_KEY = "__logged_errors__"         # Set[str] of error tags already logged

# ── C-1 fix: module-level polygon cache ──────────────────────────────────────
# Key: (polygon_json_str, coord_space, frame_size) — stable across hot-reloads
# because it's derived from rule *content*, not rule_config object identity.
# Value: compiled Shapely Polygon or False (cached failure) or None.
# The GIL makes dict reads/writes atomic in CPython; no extra lock is needed
# for cache hits (the common case). A benign double-build can occur on the
# very first frame when two threads race, but both produce the same Polygon
# and the last writer wins without corruption.
import json as _json
_POLY_CACHE: dict = {}


def _poly_cache_key(polygon_coords, coord_space: str, frame_size) -> tuple:
    """Hashable key for the module-level polygon cache."""
    try:
        poly_str = _json.dumps(polygon_coords, sort_keys=True, default=str)
    except (TypeError, ValueError):
        poly_str = str(polygon_coords)
    return (poly_str, coord_space, frame_size)


def _log_once(rule_config: Dict, tag: str, message: str, *args) -> None:
    """Emit ``logger.warning(message, *args)`` only on the first call per (rule_config, tag)."""
    seen = rule_config.get(_LOGGED_KEY)
    if seen is None:
        seen = set()
        rule_config[_LOGGED_KEY] = seen
    if tag in seen:
        return
    seen.add(tag)
    logger.warning(message, *args)


def _anchor_point(box: Dict, anchor: str) -> Tuple[float, float]:
    xmin, xmax = box["xmin"], box["xmax"]
    ymin, ymax = box["ymin"], box["ymax"]
    if anchor == "centroid":
        return ((xmin + xmax) / 2.0, (ymin + ymax) / 2.0)
    if anchor == "top_center":
        return ((xmin + xmax) / 2.0, float(ymin))
    return ((xmin + xmax) / 2.0, float(ymax))  # bottom_center (default)


def _build_polygon(
    rule_config: Dict,
    polygon_coords: List[List[float]],
    coord_space: str,
    frame_size: Optional[Tuple[int, int]],
) -> Optional[Polygon]:
    """
    Construct + validate a Shapely Polygon. Returns None on failure.
    Logs each distinct failure tag at most once per rule_config.
    """
    if not polygon_coords or len(polygon_coords) < 3:
        _log_once(rule_config, "too-few-vertices",
                  "Spatial rule polygon has fewer than 3 vertices; suppressing.")
        return None
    if len(polygon_coords) > MAX_POLYGON_VERTICES:
        _log_once(rule_config, "too-many-vertices",
                  "Spatial rule polygon has %d vertices (cap=%d); rejecting.",
                  len(polygon_coords), MAX_POLYGON_VERTICES)
        return None

    if coord_space == "normalized":
        if not frame_size:
            _log_once(rule_config, "no-frame-size",
                      "Normalized polygon supplied but no frame_size known; rejecting.")
            return None
        w, h = frame_size
        if w <= 0 or h <= 0:
            _log_once(rule_config, "bad-frame-size",
                      "Normalized polygon needs positive frame_size, got %r; rejecting.", frame_size)
            return None
        pts = []
        for v in polygon_coords:
            if len(v) != 2:
                _log_once(rule_config, "bad-vertex-shape",
                          "Polygon vertex must be [x, y]; suppressing.")
                return None
            try:
                x, y = float(v[0]), float(v[1])
            except (TypeError, ValueError):
                _log_once(rule_config, "non-numeric-vertex",
                          "Polygon vertex is non-numeric; suppressing.")
                return None
            if not (0.0 <= x <= 1.0 and 0.0 <= y <= 1.0):
                _log_once(rule_config, "normalized-out-of-range",
                          "Normalized polygon vertex %r outside [0,1]; suppressing.", v)
                return None
            pts.append((x * w, y * h))
    else:
        pts = []
        for v in polygon_coords:
            if len(v) != 2:
                _log_once(rule_config, "bad-vertex-shape",
                          "Polygon vertex must be [x, y]; suppressing.")
                return None
            try:
                pts.append((float(v[0]), float(v[1])))
            except (TypeError, ValueError):
                _log_once(rule_config, "non-numeric-vertex",
                          "Polygon vertex is non-numeric; suppressing.")
                return None

    try:
        poly = Polygon(pts)
        if not poly.is_valid:
            poly = make_valid(poly)
            if poly.geom_type != "Polygon":
                _log_once(rule_config, "unrepairable",
                          "Polygon could not be repaired to a valid Polygon (got %s).",
                          poly.geom_type)
                return None
        if poly.is_empty or poly.area <= 0:
            _log_once(rule_config, "empty-polygon",
                      "Polygon is empty or zero-area; suppressing.")
            return None
        return poly
    except Exception as e:  # noqa: BLE001
        _log_once(rule_config, "construction-failed",
                  "Failed to construct spatial polygon: %s", e)
        return None


def _get_or_build_polygon(
    rule_config: Dict,
    polygon_coords,
    coord_space: str,
    frame_size,
):
    """Return the cached Shapely Polygon for this (polygon, coord_space, frame_size)
    combination, building it on first access.

    C-1 fix: cache lives in the module-level ``_POLY_CACHE`` dict (not on
    rule_config) so compiled polygons survive hot-reloads.
    Bad configurations are cached as ``False`` so subsequent frames skip
    re-validation and re-logging.
    """
    cache_key = _poly_cache_key(
        polygon_coords,
        coord_space,
        frame_size if coord_space == "normalized" else None,
    )
    cached = _POLY_CACHE.get(cache_key)
    if cached is not None:
        return cached  # False (cached failure) or Polygon
    # Cache miss — build and store.
    poly = _build_polygon(rule_config, polygon_coords, coord_space, frame_size)
    result = poly if poly is not None else False
    _POLY_CACHE[cache_key] = result
    return result


def evaluate_spatial_rule(
    detection: Dict,
    rule_config: Dict,
    frame_size: Optional[Tuple[int, int]] = None,
) -> bool:
    """
    Evaluate a spatial / geofence rule.

    rule_config schema (post server-side validation):
      type:              "geofence"  (required)
      polygon:           [[x, y], ...]  3..MAX_POLYGON_VERTICES vertices
      coordinate_space:  "pixel" (default) | "normalized"
      mode:              "entry" (default) | "exit"
      anchor:            "bottom_center" (default) | "centroid" | "top_center"

    Returns True if the detection should be allowed through, False if suppressed.
    Unknown / malformed configurations fail closed (return False) — never silently
    alert-on-everything.
    """
    rule_type = (rule_config.get("type") or "").lower()
    if rule_type != "geofence":
        # Not our rule type — let the dispatcher decide. We don't suppress here.
        return True

    polygon_coords = rule_config.get("polygon")
    coord_space = (rule_config.get("coordinate_space") or "pixel").lower()
    mode = (rule_config.get("mode") or "entry").lower()
    anchor = (rule_config.get("anchor") or "bottom_center").lower()

    if coord_space not in _ALLOWED_SPACES:
        _log_once(rule_config, "bad-coord-space",
                  "Spatial rule has unsupported coordinate_space=%r; suppressing.", coord_space)
        return False
    if mode not in _ALLOWED_MODES:
        _log_once(rule_config, "bad-mode",
                  "Spatial rule has unsupported mode=%r; suppressing.", mode)
        return False
    if anchor not in _ALLOWED_ANCHORS:
        _log_once(rule_config, "bad-anchor",
                  "Spatial rule has unsupported anchor=%r; suppressing.", anchor)
        return False

    poly = _get_or_build_polygon(rule_config, polygon_coords, coord_space, frame_size)
    if not poly:  # None (legacy) or False (cached failure)
        return False

    try:
        x, y = _anchor_point(detection["box"], anchor)
    except (KeyError, TypeError):
        _log_once(rule_config, "missing-box",
                  "Detection missing 'box'; suppressing spatial rule.")
        return False

    # ``intersects`` returns True for points strictly inside OR on the boundary.
    # ``contains`` excludes the boundary, which would wrongly suppress detections
    # whose pixel-quantized anchor falls exactly on a polygon edge.
    inside = poly.intersects(Point(x, y))
    if mode == "entry":
        return inside        # restricted zone: violation only if inside
    return not inside        # permitted zone: violation only if outside
