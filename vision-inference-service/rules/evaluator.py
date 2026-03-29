"""
rules/evaluator.py
Evaluates complex rules (like 'person without hairnet') using spatial logic
to avoid false conclusions from disjointed bounding boxes.
"""
import logging
from typing import List, Dict

import config
from .spatial import get_head_zone, get_hand_zone, get_overlap_ratio, is_contained

logger = logging.getLogger("vision-service.rules")


def evaluate_violations(detections: List[Dict], configured_rules: List) -> List[Dict]:
    """
    Takes all raw bounding box detections from the Inference Engine,
    and cross-references them against active Camera violation rules.
    Returns a list of VALIDATED violations, complete with the matched rule text.
    """
    violations = []
    
    # 1. Filter out extremely low confidence detections right away to save compute
    # Note: Zero-shot models like owlvit often return naturally low confidence scores (0.05 - 0.2)
    valid_detections = []
    for d in detections:
        score = d.get("score", 0)
        source = d.get("source_model", "")
        
        if source == "hygiene-monitor":
            min_thresh = 0.05
        elif source == "human-detection-v1":
            min_thresh = config.MIN_CONFIDENCE_HUGGINGFACE
        else:
            # Strict thresholding for Roboflow API models
            min_thresh = config.MIN_CONFIDENCE_ROBOFLOW
            
        if score >= min_thresh:
            valid_detections.append(d)
    
    # Pre-categorize for spatial lookup speed
    persons = [d for d in valid_detections if d["label"] == "person"]
    hairnets = [d for d in valid_detections if d["label"] in ["hairnet", "hair cap", "hat", "cap"]]
    gloves = [d for d in valid_detections if d["label"] in ["glove", "gloves", "hand protection"]]

    for rule in configured_rules:
        # Complex Rule: Person without hairnet
        if "person without hairnet" in rule.trigger_labels:
            for p_idx, person in enumerate(persons):
                head_zone = get_head_zone(person["box"])
                has_hairnet = False
                
                # Check if ANY hairnet is over THIS person's head zone
                for hairnet in hairnets:
                    overlap_ratio = get_overlap_ratio(head_zone, hairnet["box"])
                    if overlap_ratio > 0.3: # If 30% of the hairnet box is in the head zone, they have a hairnet
                        has_hairnet = True
                        break
                
                if not has_hairnet:
                    logger.info("Spatial Trigger: Person %d triggered 'person without hairnet' (no hairnet in head zone)", p_idx)
                    # We flag the specific PERSON bounding box as the violation source
                    v = person.copy()
                    v["matched_rule"] = rule.name if hasattr(rule, "name") else "person without hairnet"
                    v["violation_type"] = "person without hairnet"
                    violations.append(v)
                    
        # Complex Rule: Person without gloves
        if "person without gloves" in rule.trigger_labels:
            for p_idx, person in enumerate(persons):
                hand_zone = get_hand_zone(person["box"])
                has_gloves = False
                
                for glove in gloves:
                    overlap_ratio = get_overlap_ratio(hand_zone, glove["box"])
                    if overlap_ratio > 0.3:
                        has_gloves = True
                        break
                        
                if not has_gloves:
                    logger.info("Spatial Trigger: Person %d triggered 'person without gloves' (no gloves in hand zone)", p_idx)
                    v = person.copy()
                    v["matched_rule"] = rule.name if hasattr(rule, "name") else "person without gloves"
                    v["violation_type"] = "person without gloves"
                    violations.append(v)

        # Complex Rule: Person without Hardhat (Construction Site Safety)
        if "no-hardhat" in rule.trigger_labels:
            no_hardhats = [d for d in valid_detections if d["label"] == "no-hardhat"
                           and d["source_model"] == rule.model_identifier]
            for det in no_hardhats:
                logger.info("Spatial Trigger: Found 'no-hardhat' from construction-site-safety model")
                v = det.copy()
                v["matched_rule"] = rule.name if hasattr(rule, "name") else "no-hardhat"
                v["violation_type"] = "no-hardhat"
                violations.append(v)

        # Complex Rule: Person without Safety Vest (Construction Site Safety)
        if "no-safety vest" in rule.trigger_labels:
            no_vests = [d for d in valid_detections if d["label"] in ("no-safety vest", "no-safety-vest")
                        and d["source_model"] == rule.model_identifier]
            for det in no_vests:
                logger.info("Spatial Trigger: Found 'no-safety vest' from construction-site-safety model")
                v = det.copy()
                v["matched_rule"] = rule.name if hasattr(rule, "name") else "no-safety vest"
                v["violation_type"] = "no-safety vest"
                violations.append(v)

        # Simple Fallback rules (e.g. 'dirty floor', 'trash', 'unauthorized vehicle')
        # Here we just check if the detection label exactly matches a single trigger
        for label in rule.trigger_labels:
            if label in ["person without hairnet", "person without gloves", "no-hardhat", "no-safety vest"]:
                continue # Handled by spatial logic above
                
            for det in valid_detections:
                if det["label"] == label and det["source_model"] == rule.model_identifier:
                    logger.info("Basic Trigger: Found '%s' satisfying rule", label)
                    v = det.copy()
                    v["matched_rule"] = rule.name if hasattr(rule, "name") else label
                    v["violation_type"] = label
                    violations.append(v)

    # De-duplicate violations (e.g., if multiple models trigger the same thing for the same person)
    # Using tracking id or center coordinates if available could be better, but we settle for box matching
    unique_violations = []
    seen_boxes = set()
    
    for v in violations:
        box_tuple = (v["box"]["xmin"], v["box"]["ymin"], v["box"]["xmax"], v["box"]["ymax"], v["violation_type"])
        if box_tuple not in seen_boxes:
            seen_boxes.add(box_tuple)
            unique_violations.append(v)

    if unique_violations:
        logger.info("🚨 Evaluator confirmed %d violations after applying spatial rules and deduplication.", len(unique_violations))
    else:
        logger.info("✅ Evaluator confirmed 0 violations for this frame.")

    return unique_violations
