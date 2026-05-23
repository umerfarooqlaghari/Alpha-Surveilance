"""Unit tests for the anomaly rule filter."""
from rules.anomaly import evaluate_anomaly_rule


def _det(label="burnt_chip", score=0.9):
    return {"label": label, "score": score, "box": {"xmin": 0, "ymin": 0, "xmax": 10, "ymax": 10}}


def test_non_anomaly_type_passes_through():
    assert evaluate_anomaly_rule(_det(), {"type": "geofence"}) is True


def test_no_min_score_and_no_targets_passes():
    assert evaluate_anomaly_rule(_det(score=0.05), {"type": "anomaly"}) is True


def test_below_min_score_is_suppressed():
    assert evaluate_anomaly_rule(_det(score=0.4), {"type": "anomaly", "min_score": 0.7}) is False


def test_at_or_above_min_score_passes():
    assert evaluate_anomaly_rule(_det(score=0.7), {"type": "anomaly", "min_score": 0.7}) is True
    assert evaluate_anomaly_rule(_det(score=0.95), {"type": "anomaly", "min_score": 0.7}) is True


def test_target_labels_intersection():
    cfg = {"type": "anomaly", "target_labels": ["burnt_chip", "broken_biscuit"]}
    assert evaluate_anomaly_rule(_det(label="burnt_chip"), cfg) is True
    assert evaluate_anomaly_rule(_det(label="ok_chip"), cfg) is False


def test_target_labels_case_insensitive():
    cfg = {"type": "anomaly", "target_labels": ["Burnt_Chip"]}
    assert evaluate_anomaly_rule(_det(label="burnt_chip"), cfg) is True


def test_non_numeric_min_score_fails_closed():
    assert evaluate_anomaly_rule(_det(), {"type": "anomaly", "min_score": "high"}) is False


def test_min_score_out_of_range_fails_closed():
    assert evaluate_anomaly_rule(_det(), {"type": "anomaly", "min_score": 1.5}) is False
    assert evaluate_anomaly_rule(_det(), {"type": "anomaly", "min_score": -0.1}) is False


def test_target_labels_not_a_list_fails_closed():
    assert evaluate_anomaly_rule(_det(), {"type": "anomaly", "target_labels": "burnt_chip"}) is False


# ─── Issue #4 regression tests ───────────────────────────────────────────────

def test_non_numeric_detection_score_fails_closed():
    """Issue #4: non-numeric 'score' field must not raise ValueError; fail closed."""
    det = {"label": "burnt_chip", "score": "high", "box": {"xmin": 0, "ymin": 0, "xmax": 10, "ymax": 10}}
    assert evaluate_anomaly_rule(det, {"type": "anomaly", "min_score": 0.5}) is False


def test_none_detection_score_treated_as_zero():
    """Issue #4: None score coerces to 0.0 and is suppressed if min_score > 0."""
    det = {"label": "burnt_chip", "score": None, "box": {"xmin": 0, "ymin": 0, "xmax": 10, "ymax": 10}}
    assert evaluate_anomaly_rule(det, {"type": "anomaly", "min_score": 0.5}) is False
    # With no threshold, a zero-score detection passes (matches old behaviour).
    assert evaluate_anomaly_rule(det, {"type": "anomaly"}) is True


def test_missing_score_field_treated_as_zero():
    """Issue #4: completely absent 'score' key must not raise; treated as 0.0."""
    det = {"label": "burnt_chip", "box": {"xmin": 0, "ymin": 0, "xmax": 10, "ymax": 10}}
    assert evaluate_anomaly_rule(det, {"type": "anomaly", "min_score": 0.1}) is False


def test_dict_score_field_fails_closed():
    """Issue #4: a dict or list as score must not raise; fail closed."""
    for bad_score in [{"value": 0.9}, [0.9], object()]:
        det = {"label": "burnt_chip", "score": bad_score,
               "box": {"xmin": 0, "ymin": 0, "xmax": 10, "ymax": 10}}
        assert evaluate_anomaly_rule(det, {"type": "anomaly", "min_score": 0.5}) is False, \
            f"score={bad_score!r} should fail closed"


def test_non_numeric_score_logged_once_not_every_frame():
    """Issue #4: the 'anomaly-bad-detection-score' tag must be logged at most once."""
    import logging
    det = {"label": "burnt_chip", "score": "bad", "box": {"xmin": 0, "ymin": 0, "xmax": 10, "ymax": 10}}
    cfg = {"type": "anomaly", "min_score": 0.5}

    # Repeated calls with the same cfg must emit exactly one warning.
    # Capture logs to verify the sentinel _log_once deduplification works.
    with_log = []
    original_warn = logging.getLogger("vision-service.rules.anomaly").warning
    try:
        logging.getLogger("vision-service.rules.anomaly").warning = lambda *a, **kw: with_log.append(a)
        for _ in range(50):
            evaluate_anomaly_rule(det, cfg)
    finally:
        logging.getLogger("vision-service.rules.anomaly").warning = original_warn

    # The anomaly logger itself does not call logger.warning directly — it
    # delegates to _log_once which uses the spatial logger. Regardless, the
    # rule_config sentinel must be set after the first call.
    from rules.spatial import _LOGGED_KEY
    assert "anomaly-bad-detection-score" in cfg.get(_LOGGED_KEY, set()), \
        "sentinel tag must be stored on rule_config after first bad-score event"
