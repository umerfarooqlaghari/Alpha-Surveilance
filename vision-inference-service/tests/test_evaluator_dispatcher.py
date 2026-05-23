"""Unit tests for evaluator's rule_config dispatcher (fail-closed on unknown type)."""
from types import SimpleNamespace

from rules.evaluator import evaluate_violations


def _rule(label, model="human-detection-v1", sop_id="sop-1", rule_config=None):
    return SimpleNamespace(
        sop_violation_type_id=sop_id,
        model_identifier=model,
        trigger_labels=[label],
        rule_config=rule_config or {},
    )


def _person_det():
    return {
        "label": "person",
        "score": 0.95,
        "box": {"xmin": 10, "ymin": 10, "xmax": 50, "ymax": 80},
        "source_model": "human-detection-v1",
    }


def test_no_rule_config_allows_detection():
    violations = evaluate_violations([_person_det()], [_rule("person")])
    assert len(violations) == 1


def test_unknown_rule_type_fails_closed():
    rule = _rule("person", rule_config={"type": "loitering", "duration_s": 30})
    violations = evaluate_violations([_person_det()], [rule])
    assert violations == []


def test_geofence_inside_emits_violation():
    rule = _rule(
        "person",
        rule_config={
            "type": "geofence",
            "polygon": [[0, 0], [100, 0], [100, 100], [0, 100]],
            "mode": "entry",
        },
    )
    violations = evaluate_violations([_person_det()], [rule])
    assert len(violations) == 1


def test_geofence_outside_suppresses():
    rule = _rule(
        "person",
        rule_config={
            "type": "geofence",
            "polygon": [[500, 500], [600, 500], [600, 600], [500, 600]],
            "mode": "entry",
        },
    )
    violations = evaluate_violations([_person_det()], [rule])
    assert violations == []


def test_malformed_geofence_fails_closed_not_open():
    # Previous bug: empty polygon caused is_point_in_polygon -> True -> alert
    # on every detection. Now it must SUPPRESS.
    rule = _rule("person", rule_config={"type": "geofence", "polygon": []})
    violations = evaluate_violations([_person_det()], [rule])
    assert violations == []
