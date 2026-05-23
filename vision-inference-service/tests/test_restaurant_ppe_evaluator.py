from types import SimpleNamespace

from rules.evaluator import evaluate_violations


def _rule(label, sop_id="sop-1", model="restaurant-ppe-v1"):
    return SimpleNamespace(
        sop_violation_type_id=sop_id,
        model_identifier=model,
        trigger_labels=[label],
    )


def test_restaurant_ppe_uses_direct_model_hairnet_violation():
    detections = [
        {
            "label": "no-hairnet",
            "score": 0.91,
            "box": {"xmin": 10, "ymin": 20, "xmax": 80, "ymax": 110},
            "source_model": "restaurant-ppe-v1",
            "model_family": "restaurant-ppe",
        }
    ]

    violations = evaluate_violations(detections, [_rule("person without hairnet")])

    assert len(violations) == 1
    assert violations[0]["label"] == "person without hairnet"
    assert violations[0]["model_label"] == "no-hairnet"
    assert violations[0]["sop_violation_type_id"] == "sop-1"


def test_restaurant_ppe_does_not_infer_mask_from_back_of_head():
    detections = [
        {
            "label": "back-of-head",
            "score": 0.99,
            "box": {"xmin": 10, "ymin": 20, "xmax": 80, "ymax": 110},
            "source_model": "restaurant-ppe-v1",
            "model_family": "restaurant-ppe",
        }
    ]

    violations = evaluate_violations(detections, [_rule("no-mask", sop_id="sop-mask")])

    assert violations == []


def test_restaurant_ppe_supports_legacy_model_identifier_alias():
    detections = [
        {
            "label": "visible-face-no-mask",
            "score": 0.88,
            "box": {"xmin": 30, "ymin": 40, "xmax": 120, "ymax": 160},
            "source_model": "restaurant-hygiene-v1",
            "model_family": "restaurant-ppe",
        }
    ]

    violations = evaluate_violations(
        detections,
        [_rule("person without mask", sop_id="sop-mask", model="restaurant-hygiene-v1")],
    )

    assert len(violations) == 1
    assert violations[0]["label"] == "person without mask"
    assert violations[0]["model_label"] == "visible-face-no-mask"
