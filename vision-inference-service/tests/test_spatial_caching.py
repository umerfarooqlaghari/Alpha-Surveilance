"""
Tests for the hot-path optimizations in rules.spatial:
  * compiled-Polygon caching in the module-level ``_POLY_CACHE`` (survives hot-reloads)
  * log-once-per-rule for malformed configurations
"""
import logging
from unittest.mock import patch

import pytest
from shapely.geometry import Polygon

from rules import spatial
from rules.spatial import _LOGGED_KEY, _POLY_CACHE, _poly_cache_key, evaluate_spatial_rule


SQUARE = [[100, 100], [200, 100], [200, 200], [100, 200]]


@pytest.fixture(autouse=True)
def _isolate_poly_cache():
    """Module-level cache must be empty between tests so caching assertions are deterministic."""
    _POLY_CACHE.clear()
    yield
    _POLY_CACHE.clear()


def _det(xmin, ymin, xmax, ymax, score=0.9):
    return {"label": "person", "score": score,
            "box": {"xmin": xmin, "ymin": ymin, "xmax": xmax, "ymax": ymax}}


def test_polygon_is_cached_in_module_cache_after_first_call():
    cfg = {"type": "geofence", "polygon": SQUARE, "mode": "entry"}
    evaluate_spatial_rule(_det(140, 50, 160, 150), cfg)
    key = _poly_cache_key(SQUARE, "pixel", None)
    assert key in _POLY_CACHE
    assert isinstance(_POLY_CACHE[key], Polygon)


def test_polygon_not_rebuilt_on_subsequent_calls():
    cfg = {"type": "geofence", "polygon": SQUARE, "mode": "entry"}
    evaluate_spatial_rule(_det(140, 50, 160, 150), cfg)

    with patch.object(spatial, "Polygon", side_effect=AssertionError("must not be called twice")):
        # Hot-path: must NOT touch shapely.Polygon again.
        for _ in range(100):
            assert evaluate_spatial_rule(_det(140, 50, 160, 150), cfg) is True


def test_normalized_polygon_caches_per_frame_size():
    norm_square = [[0.25, 0.25], [0.5, 0.25], [0.5, 0.5], [0.25, 0.5]]
    cfg = {"type": "geofence", "polygon": norm_square, "coordinate_space": "normalized"}

    # Two different frame sizes should produce two cache entries.
    evaluate_spatial_rule(_det(140, 50, 160, 150), cfg, frame_size=(400, 400))
    evaluate_spatial_rule(_det(140, 50, 160, 150), cfg, frame_size=(800, 800))

    k1 = _poly_cache_key(norm_square, "normalized", (400, 400))
    k2 = _poly_cache_key(norm_square, "normalized", (800, 800))
    assert k1 in _POLY_CACHE
    assert k2 in _POLY_CACHE
    assert _POLY_CACHE[k1].area != _POLY_CACHE[k2].area


def test_polygon_cache_survives_rule_config_replacement():
    """C-1 regression: replacing the rule_config dict (as happens on hot-reload)
    must NOT invalidate the compiled-polygon cache, because the cache is keyed
    by polygon content rather than rule_config identity."""
    cfg1 = {"type": "geofence", "polygon": SQUARE, "mode": "entry"}
    evaluate_spatial_rule(_det(140, 50, 160, 150), cfg1)

    cfg2 = {"type": "geofence", "polygon": SQUARE, "mode": "entry"}  # fresh dict, same polygon
    with patch.object(spatial, "Polygon", side_effect=AssertionError("must not rebuild")):
        assert evaluate_spatial_rule(_det(140, 50, 160, 150), cfg2) is True


def test_bad_config_is_cached_as_false_not_recomputed():
    cfg = {"type": "geofence", "polygon": [[100, 100], [200, 200]]}  # only 2 vertices

    assert evaluate_spatial_rule(_det(0, 0, 10, 10), cfg) is False
    key = _poly_cache_key([[100, 100], [200, 200]], "pixel", None)
    assert _POLY_CACHE[key] is False

    # Subsequent calls must NOT re-run validation (which would log again).
    with patch.object(spatial, "_build_polygon", side_effect=AssertionError("must not run")):
        for _ in range(50):
            assert evaluate_spatial_rule(_det(0, 0, 10, 10), cfg) is False


def test_validation_error_logged_once_per_rule_config(caplog):
    cfg = {"type": "geofence", "polygon": [[100, 100], [200, 200]]}  # too few vertices

    with caplog.at_level(logging.WARNING, logger="vision-service.rules.spatial"):
        for _ in range(100):
            evaluate_spatial_rule(_det(0, 0, 10, 10), cfg)

    matching = [r for r in caplog.records if "fewer than 3 vertices" in r.getMessage()]
    assert len(matching) == 1, f"expected exactly 1 warning, got {len(matching)}"
    assert _LOGGED_KEY in cfg
    assert "too-few-vertices" in cfg[_LOGGED_KEY]


def test_log_once_per_unique_polygon_content(caplog):
    """C-1 fix changed log-once scope: the cache is now keyed by polygon
    content rather than rule_config identity, so two rule_config dicts with
    the SAME malformed polygon log a single warning between them —
    a stale failure for the same polygon shouldn't keep re-spamming logs
    just because someone reloaded the camera."""
    bad1 = {"type": "geofence", "polygon": [[100, 100], [200, 200]]}
    bad2 = {"type": "geofence", "polygon": [[100, 100], [200, 200]]}  # same content

    with caplog.at_level(logging.WARNING, logger="vision-service.rules.spatial"):
        evaluate_spatial_rule(_det(0, 0, 10, 10), bad1)
        evaluate_spatial_rule(_det(0, 0, 10, 10), bad2)

    matching = [r for r in caplog.records if "fewer than 3 vertices" in r.getMessage()]
    # Same polygon content → cached failure → second call short-circuits without logging.
    assert len(matching) == 1


def test_distinct_error_tags_each_log_once(caplog):
    # Same rule_config, but different fields are bad -> each tag emits exactly one warning.
    cfg = {"type": "geofence", "polygon": SQUARE, "mode": "kazooie", "anchor": "elbow"}

    with caplog.at_level(logging.WARNING, logger="vision-service.rules.spatial"):
        # First call hits "bad-mode" and short-circuits before "bad-anchor".
        evaluate_spatial_rule(_det(0, 0, 10, 10), cfg)
        evaluate_spatial_rule(_det(0, 0, 10, 10), cfg)
        evaluate_spatial_rule(_det(0, 0, 10, 10), cfg)

    mode_warnings = [r for r in caplog.records if "unsupported mode" in r.getMessage()]
    assert len(mode_warnings) == 1
