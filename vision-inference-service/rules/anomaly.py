"""
rules/anomaly.py

Anomaly (defect / damage) policy filter.

Behavior contract used by ``rules.evaluator``:
  - Return True  -> detection PASSES the anomaly filter.
  - Return False -> detection is SUPPRESSED.

The evaluator already filters by ``trigger_labels``, so this filter focuses on
*score* thresholding plus an optional defense-in-depth ``target_labels``
intersection. Malformed configs fail closed.

Validation errors are logged once per rule_config (see rules.spatial._log_once)
to avoid log spam at 30 FPS x N detections.
"""
import logging
from typing import Dict

from rules.spatial import _log_once

logger = logging.getLogger("vision-service.rules.anomaly")


def evaluate_anomaly_rule(detection: Dict, rule_config: Dict) -> bool:
    rule_type = (rule_config.get("type") or "").lower()
    if rule_type != "anomaly":
        return True  # not our type — pass through

    min_score_raw = rule_config.get("min_score", 0.0)
    try:
        min_score = float(min_score_raw)
    except (TypeError, ValueError):
        _log_once(rule_config, "anomaly-bad-min-score-type",
                  "Anomaly rule has non-numeric min_score=%r; suppressing.", min_score_raw)
        return False
    if not (0.0 <= min_score <= 1.0):
        _log_once(rule_config, "anomaly-min-score-out-of-range",
                  "Anomaly rule min_score=%r out of [0,1]; suppressing.", min_score_raw)
        return False

    try:
        score = float(detection.get("score") or 0.0)
    except (TypeError, ValueError):
        _log_once(rule_config, "anomaly-bad-detection-score",
                  "Anomaly rule received non-numeric detection score=%r; suppressing.",
                  detection.get("score"))
        return False
    if score < min_score:
        return False

    target_labels = rule_config.get("target_labels") or []
    if target_labels:
        if not isinstance(target_labels, (list, tuple)):
            _log_once(rule_config, "anomaly-bad-target-labels",
                      "Anomaly rule target_labels must be a list; suppressing.")
            return False
        normalized_targets = {str(t).strip().lower() for t in target_labels if str(t).strip()}
        label = str(detection.get("label", "")).strip().lower()
        if label not in normalized_targets:
            return False

    return True
