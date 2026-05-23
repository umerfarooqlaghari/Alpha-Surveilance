"""
rules/evaluator.py

Converts raw detector outputs into configured SOP violations.

Restaurant PPE violation rules are intentionally direct detections only. The
old person-box head/face estimation path was removed because it produced fragile
results for side views, back-facing workers, tilted cameras, and crowded scenes.
"""
import logging
from typing import Dict, Iterable, List, Optional, Set, Tuple

import config
from inference.restaurant_ppe import MODEL_IDS as RESTAURANT_PPE_MODEL_IDS
from inference.restaurant_ppe import normalize_violation_label
from rules.spatial import evaluate_spatial_rule, _log_once
from rules.anomaly import evaluate_anomaly_rule
from rules.dwell import evaluate_dwell_rule

logger = logging.getLogger("vision-service.rules")


# Whitelist of supported rule_config.type values. Anything outside this set is
# treated as a misconfiguration and fails CLOSED (detection suppressed) to avoid
# the previous fail-open bug where unknown types turned a camera into
# "alert on every detection".
SUPPORTED_RULE_TYPES = {"geofence", "anomaly", "dwell"}


MODEL_SOURCE_ALIASES = {
    "construction-site-safety-v1": {"construction-site-safety-v1", "construction-site-safety/1"},
    "restaurant-ppe-v1": {"restaurant-ppe-v1", "restaurant-hygiene-v1"},
    "restaurant-hygiene-v1": {"restaurant-hygiene-v1", "restaurant-ppe-v1"},
}

RESTAURANT_TRIGGER_ALIASES = {
    "person without hairnet": "no-hairnet",
    "person-without-hairnet": "no-hairnet",
    "no hairnet": "no-hairnet",
    "no-hairnet": "no-hairnet",
    "missing hairnet": "no-hairnet",
    "missing-hairnet": "no-hairnet",
    "person without mask": "no-mask",
    "person-without-mask": "no-mask",
    "no mask": "no-mask",
    "no-mask": "no-mask",
    "missing mask": "no-mask",
    "missing-mask": "no-mask",
    "no face cover": "no-mask",
    "no-face-cover": "no-mask",
    "person without glove": "no-glove",
    "person without gloves": "no-glove",
    "person-without-glove": "no-glove",
    "person-without-gloves": "no-glove",
    "no glove": "no-glove",
    "no gloves": "no-glove",
    "no-glove": "no-glove",
    "no-gloves": "no-glove",
    "missing glove": "no-glove",
    "missing gloves": "no-glove",
    "missing-glove": "no-glove",
    "missing-gloves": "no-glove",
    "incorrect mask": "incorrect-mask",
    "incorrect-mask": "incorrect-mask",
    "improper mask": "incorrect-mask",
    "improper-mask": "incorrect-mask",
    "mask below nose": "incorrect-mask",
    "mask-below-nose": "incorrect-mask",
}


def _rule_attr(rule, name: str, default=None):
    if isinstance(rule, dict):
        return rule.get(name, default)
    return getattr(rule, name, default)


def _rule_labels(rule) -> List[str]:
    labels = _rule_attr(rule, "trigger_labels", []) or []
    return [str(label).strip().lower() for label in labels if str(label).strip()]


def _canonical_label(label: str) -> str:
    return str(label or "").strip().lower().replace("_", "-").replace(" ", "-")


def _source_matches(det_source: str, rule_model: str) -> bool:
    allowed_sources = MODEL_SOURCE_ALIASES.get(rule_model, {rule_model})
    return det_source in allowed_sources


def _confidence_threshold(det: Dict) -> float:
    source = det.get("source_model", "")
    if det.get("model_family") == "restaurant-ppe" or source in RESTAURANT_PPE_MODEL_IDS:
        return config.MIN_CONFIDENCE_RESTAURANT_PPE
    if source == "human-detection-v1":
        return config.MIN_CONFIDENCE_HUGGINGFACE
    return config.MIN_CONFIDENCE_ROBOFLOW


def _valid_detections(detections: Iterable[Dict]) -> List[Dict]:
    valid = []
    for det in detections:
        if det.get("score", 0) >= _confidence_threshold(det):
            valid.append(det)
    return valid


def _restaurant_targets(rule_labels: List[str]) -> Dict[str, str]:
    """
    Returns canonical model labels mapped to the configured trigger label to emit.

        Example:
            "person without hairnet" -> {"no-hairnet": "person without hairnet"}
            "no-mask" -> {"no-mask": "no-mask"}
    """
    if not rule_labels:
                return {
                        "no-hairnet": "no-hairnet",
                        "no-mask": "no-mask",
                        "no-glove": "no-glove",
                        "incorrect-mask": "incorrect-mask",
                }

    targets: Dict[str, str] = {}
    for trigger in rule_labels:
        normalized_trigger = trigger.replace("_", "-")
        canonical = RESTAURANT_TRIGGER_ALIASES.get(normalized_trigger)
        if canonical:
            targets[canonical] = trigger
    return targets


def _attach_rule_metadata(det: Dict, rule, emitted_label: str) -> Dict:
    violation = det.copy()
    violation["matched_rule"] = _rule_attr(rule, "name", emitted_label)
    violation["violation_type"] = emitted_label
    violation["label"] = emitted_label
    violation["source_model"] = _rule_attr(rule, "model_identifier")

    sop_id = _rule_attr(rule, "sop_violation_type_id")
    if sop_id:
        violation["sop_violation_type_id"] = sop_id

    if det.get("label") != emitted_label:
        violation["model_label"] = det.get("label")

    return violation


def _dedupe(violations: List[Dict]) -> List[Dict]:
    unique_violations = []
    seen_boxes: Set[tuple] = set()

    for violation in violations:
        box = violation["box"]
        box_tuple = (
            box["xmin"],
            box["ymin"],
            box["xmax"],
            box["ymax"],
            violation["violation_type"],
            violation.get("sop_violation_type_id"),
        )
        if box_tuple not in seen_boxes:
            seen_boxes.add(box_tuple)
            unique_violations.append(violation)

    return unique_violations


def _passes_rule_config(
    det: Dict,
    rule_config: Optional[Dict],
    frame_size: Optional[Tuple[int, int]],
    camera_id: str = "",
) -> bool:
    """
    Central dispatcher for `rule_config` policy filters.

    Returns True iff the detection passes the configured filter (or no filter
    is configured). Unknown types fail CLOSED.

    Audit P4 #15: ``require_person`` is a per-rule boolean that suppresses
    a detection unless the inference engine attached a ``person_box`` to it
    (i.e., a YOLOv11n person pre-detection vouched for there being a human
    in that region). Use this for PPE rules where a no-glove on a hand-shaped
    blob without a body around it is almost certainly a false positive.
    Defaults to ``False`` so existing rules are unaffected.
    """
    if not rule_config:
        return True  # no policy attached -> allow

    # Independent of `type`, honour require_person if explicitly set.
    if rule_config.get("require_person") is True and "person_box" not in det:
        return False

    rule_type = (rule_config.get("type") or "").lower()
    if not rule_type:
        # type missing entirely — treat as "no policy" (allow). The server-side
        # validator rejects this shape on save, so reaching here means legacy data.
        return True

    if rule_type not in SUPPORTED_RULE_TYPES:
        _log_once(rule_config, "unknown-rule-type",
                  "Rule config has unsupported type=%r; suppressing detection (fail-closed).",
                  rule_type)
        return False

    if rule_type == "geofence":
        return evaluate_spatial_rule(det, rule_config, frame_size=frame_size)
    if rule_type == "anomaly":
        return evaluate_anomaly_rule(det, rule_config)
    if rule_type == "dwell":
        return evaluate_dwell_rule(det, rule_config, frame_size=frame_size, camera_id=camera_id)

    return False  # unreachable; defensive default


def evaluate_violations(
    detections: List[Dict],
    configured_rules: List,
    frame_size: Optional[Tuple[int, int]] = None,
    camera_id: str = "",
) -> List[Dict]:
    """
    Cross-reference raw detector outputs against active camera rules.

    Args:
      detections: raw detector outputs.
      configured_rules: per-camera rule list (each may carry a `rule_config` dict).
      frame_size: optional (width, height) of the frame the detections came from.
                  Required when any rule uses ``coordinate_space = "normalized"``.

    For restaurant PPE models, raw detections are already final violation
    decisions from the fine-tuned model. The evaluator only checks configured
    SOP labels, confidence, and source model.
    """
    valid_detections = _valid_detections(detections)
    violations: List[Dict] = []

    for rule in configured_rules:
        rule_model = _rule_attr(rule, "model_identifier")
        trigger_labels = _rule_labels(rule)
        rule_config = _rule_attr(rule, "rule_config", {}) or {}

        if rule_model in RESTAURANT_PPE_MODEL_IDS:
            targets = _restaurant_targets(trigger_labels)
            if not targets:
                logger.warning(
                    "Restaurant PPE rule has unsupported labels: %s. "
                    "Use no-hairnet, no-mask, no-glove, or incorrect-mask.",
                    trigger_labels,
                )
                continue

            for det in valid_detections:
                if not _source_matches(det.get("source_model", ""), rule_model):
                    continue

                canonical_model_label = normalize_violation_label(det.get("label")) or _canonical_label(det.get("label"))
                if canonical_model_label in targets:
                    if not _passes_rule_config(det, rule_config, frame_size, camera_id=camera_id):
                        continue
                    emitted_label = targets[canonical_model_label]
                    violations.append(_attach_rule_metadata(det, rule, emitted_label))
            continue

        for trigger in trigger_labels:
            canonical_trigger = _canonical_label(trigger)
            for det in valid_detections:
                if not _source_matches(det.get("source_model", ""), rule_model):
                    continue
                if _canonical_label(det.get("label")) == canonical_trigger:
                    if not _passes_rule_config(det, rule_config, frame_size, camera_id=camera_id):
                        continue
                    violations.append(_attach_rule_metadata(det, rule, trigger))

    unique_violations = _dedupe(violations)

    if unique_violations:
        logger.info("Evaluator confirmed %d direct model violation(s).", len(unique_violations))
    else:
        logger.info("Evaluator confirmed 0 violations for this frame.")

    return unique_violations
