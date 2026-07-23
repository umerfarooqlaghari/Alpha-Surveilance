"""
rules/access_control.py

Modular access control evaluator for Level 1 and Level 2 camera authorization.

Level 1 (Company-wide Unauthorized):
  Emits a violation if any person in the camera area is NOT a recognized company employee
  (e.g., identity_info has no employeeId or is marked isUnauthorized=True).

Level 2 (Area/Zone Restricted):
  Emits a violation even if the person IS a recognized company employee, if they are restricted
  from entering the specific camera area. Restricted access can be configured via:
  - Explicit employee IDs: `allowed_employee_ids` / `restricted_employee_ids`
  - Employee pools: `allowed_pools` / `restricted_pools`
"""

import logging
from typing import Dict, List, Optional, Set, Tuple

logger = logging.getLogger("vision-service.rules.access_control")

LEVEL_1_LABEL = "unauthorized_outsider"
LEVEL_2_LABEL = "restricted_area_access"


def _to_set(val) -> Set[str]:
    """Helper to convert list or single string into a set of lowercased stripped strings."""
    if not val:
        return set()
    if isinstance(val, (list, tuple, set)):
        return {str(item).strip().lower() for item in val if str(item).strip()}
    return {str(val).strip().lower()}


def evaluate_access_level_1(identity_info: Optional[Dict]) -> Tuple[bool, Optional[str]]:
    """
    Evaluates Level 1 Access Control:
    Returns (is_violation, violation_label).
    Level 1 checks if the person is an unauthorized outsider (not a company employee).
    """
    if not identity_info:
        # Default to fail-safe: if identity info is missing, treat as unknown/unauthorized
        return True, LEVEL_1_LABEL

    employee_id = identity_info.get("employeeId")
    is_unauthorized = identity_info.get("isUnauthorized", False)

    # If no employeeId is present or explicitly marked unauthorized
    if not employee_id or is_unauthorized:
        return True, LEVEL_1_LABEL

    return False, None


def evaluate_access_level_2(
    identity_info: Optional[Dict],
    rule_config: Optional[Dict]
) -> Tuple[bool, Optional[str]]:
    """
    Evaluates Level 2 Access Control:
    Returns (is_violation, violation_label).

    Checks:
    1. Outsiders (non-employees) are automatically violations under Level 2.
    2. Restricted employee lists (`restricted_employee_ids`) & restricted pools (`restricted_pools`).
    3. Allowed employee lists (`allowed_employee_ids`) & allowed pools (`allowed_pools`).
    """
    rule_cfg = rule_config or {}

    # Step 1: An outsider is always a violation in a Level 2 restricted area
    is_level_1_violation, _ = evaluate_access_level_1(identity_info)
    if is_level_1_violation:
        return True, LEVEL_2_LABEL

    employee_id = str(identity_info.get("employeeId", "")).strip().lower()
    employee_pools = _to_set(identity_info.get("pools", []))

    # Parse config sets
    allowed_ids = _to_set(rule_cfg.get("allowed_employee_ids"))
    restricted_ids = _to_set(rule_cfg.get("restricted_employee_ids"))
    allowed_pools = _to_set(rule_cfg.get("allowed_pools"))
    restricted_pools = _to_set(rule_cfg.get("restricted_pools"))

    # Step 2: Explicit restrictions check (Blacklists)
    if employee_id and employee_id in restricted_ids:
        logger.info(
            "Access Control L2 Violation: Employee '%s' is explicitly restricted in '%s'.",
            employee_id, restricted_ids
        )
        return True, LEVEL_2_LABEL

    if employee_pools and restricted_pools and not employee_pools.isdisjoint(restricted_pools):
        overlap = employee_pools.intersection(restricted_pools)
        logger.info(
            "Access Control L2 Violation: Employee '%s' belongs to restricted pool(s) %s.",
            employee_id, overlap
        )
        return True, LEVEL_2_LABEL

    # Step 3: Explicit permissions check (Whitelists)
    # If allowed_employee_ids is non-empty, employee MUST be in allowed_employee_ids
    if allowed_ids and employee_id not in allowed_ids:
        logger.info(
            "Access Control L2 Violation: Employee '%s' is not in allowed employee list %s.",
            employee_id, allowed_ids
        )
        return True, LEVEL_2_LABEL

    # If allowed_pools is non-empty, employee MUST belong to at least one allowed pool
    if allowed_pools:
        if not employee_pools or employee_pools.isdisjoint(allowed_pools):
            logger.info(
                "Access Control L2 Violation: Employee '%s' pools %s do not match allowed pools %s.",
                employee_id, employee_pools, allowed_pools
            )
            return True, LEVEL_2_LABEL

    # All checks passed: employee is authorized for this area
    return False, None


def evaluate_access_control_rule(
    det: Dict,
    rule_config: Optional[Dict],
    identity_info: Optional[Dict] = None
) -> Tuple[bool, Optional[str]]:
    """
    Main entry point for Access Control rule evaluation.

    Args:
        det: Detection dictionary.
        rule_config: Rule configuration dict attached to camera rule.
        identity_info: Identity metadata (from identify_person or det).

    Returns:
        (is_violation, violation_label)
    """
    rule_cfg = rule_config or {}

    # Extract level from config (default to level 1 if not specified)
    level_raw = rule_cfg.get("level", 1)
    if isinstance(level_raw, str):
        level_str = level_raw.strip().lower()
        level = 2 if "2" in level_str else 1
    else:
        level = 2 if level_raw == 2 else 1

    # Extract identity_info from det if not passed directly
    if not identity_info:
        identity_info = det.get("identity_info") or {
            "employeeId": det.get("employeeId"),
            "isUnauthorized": det.get("isUnauthorized", False),
            "pools": det.get("pools", []),
        }

    if level == 2:
        return evaluate_access_level_2(identity_info, rule_cfg)
    else:
        return evaluate_access_level_1(identity_info)
