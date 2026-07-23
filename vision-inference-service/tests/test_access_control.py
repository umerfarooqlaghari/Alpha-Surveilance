"""
tests/test_access_control.py

Unit tests for Level 1 and Level 2 Access Control rules in rules/access_control.py
and integration with rules/evaluator.py.
"""

import pytest
from rules.access_control import (
    evaluate_access_level_1,
    evaluate_access_level_2,
    evaluate_access_control_rule,
    LEVEL_1_LABEL,
    LEVEL_2_LABEL,
)
from rules.evaluator import _passes_rule_config, SUPPORTED_RULE_TYPES


def test_supported_rule_types_includes_access_control():
    assert "access_control" in SUPPORTED_RULE_TYPES
    assert "unauthorized_access" in SUPPORTED_RULE_TYPES


def test_level_1_outsider_detection():
    # Test missing identity info
    is_viol, label = evaluate_access_level_1(None)
    assert is_viol is True
    assert label == LEVEL_1_LABEL

    # Test unknown identity (isUnauthorized = True)
    is_viol, label = evaluate_access_level_1({"employeeId": None, "isUnauthorized": True})
    assert is_viol is True
    assert label == LEVEL_1_LABEL

    # Test empty employeeId string
    is_viol, label = evaluate_access_level_1({"employeeId": "", "isUnauthorized": False})
    assert is_viol is True
    assert label == LEVEL_1_LABEL


def test_level_1_authorized_employee():
    identity = {"employeeId": "EMP-001", "isUnauthorized": False}
    is_viol, label = evaluate_access_level_1(identity)
    assert is_viol is False
    assert label is None


def test_level_2_outsider_rejected():
    # Outsider in Level 2 area should trigger Level 2 violation
    identity = {"employeeId": None, "isUnauthorized": True}
    is_viol, label = evaluate_access_level_2(identity, {"allowed_employee_ids": ["EMP-001"]})
    assert is_viol is True
    assert label == LEVEL_2_LABEL


def test_level_2_restricted_employee_ids():
    rule_config = {
        "restricted_employee_ids": ["EMP-999", "EMP-888"]
    }
    # Restricted employee -> violation
    identity_bad = {"employeeId": "EMP-999", "isUnauthorized": False}
    is_viol, label = evaluate_access_level_2(identity_bad, rule_config)
    assert is_viol is True
    assert label == LEVEL_2_LABEL

    # Non-restricted employee -> pass
    identity_good = {"employeeId": "EMP-001", "isUnauthorized": False}
    is_viol, label = evaluate_access_level_2(identity_good, rule_config)
    assert is_viol is False
    assert label is None


def test_level_2_restricted_pools():
    rule_config = {
        "restricted_pools": ["cleaning_staff", "contractors"]
    }
    # Employee in restricted pool -> violation
    identity_sweeper = {"employeeId": "EMP-500", "isUnauthorized": False, "pools": ["cleaning_staff"]}
    is_viol, label = evaluate_access_level_2(identity_sweeper, rule_config)
    assert is_viol is True
    assert label == LEVEL_2_LABEL

    # Employee in allowed pool -> pass
    identity_vault_mgr = {"employeeId": "EMP-100", "isUnauthorized": False, "pools": ["vault_managers"]}
    is_viol, label = evaluate_access_level_2(identity_vault_mgr, rule_config)
    assert is_viol is False
    assert label is None


def test_level_2_allowed_employee_ids_whitelist():
    rule_config = {
        "allowed_employee_ids": ["EMP-001", "EMP-002"]
    }
    # Employee on whitelist -> pass
    identity_allowed = {"employeeId": "EMP-001", "isUnauthorized": False}
    is_viol, label = evaluate_access_level_2(identity_allowed, rule_config)
    assert is_viol is False
    assert label is None

    # Employee NOT on whitelist -> violation
    identity_unallowed = {"employeeId": "EMP-003", "isUnauthorized": False}
    is_viol, label = evaluate_access_level_2(identity_unallowed, rule_config)
    assert is_viol is True
    assert label == LEVEL_2_LABEL


def test_level_2_allowed_pools_whitelist():
    rule_config = {
        "allowed_pools": ["vault_pool", "exec_pool"]
    }
    # Employee in allowed pool -> pass
    identity_exec = {"employeeId": "EMP-007", "isUnauthorized": False, "pools": ["exec_pool"]}
    is_viol, label = evaluate_access_level_2(identity_exec, rule_config)
    assert is_viol is False
    assert label is None

    # Employee in non-allowed pool -> violation
    identity_finance = {"employeeId": "EMP-020", "isUnauthorized": False, "pools": ["finance_pool"]}
    is_viol, label = evaluate_access_level_2(identity_finance, rule_config)
    assert is_viol is True
    assert label == LEVEL_2_LABEL


def test_access_control_rule_dispatcher():
    det = {
        "label": "person",
        "score": 0.9,
        "box": {"xmin": 0, "ymin": 0, "xmax": 100, "ymax": 100},
        "identity_info": {"employeeId": "EMP-001", "isUnauthorized": False, "pools": ["cleaning_staff"]}
    }

    # Level 1 test via evaluate_access_control_rule
    is_viol, label = evaluate_access_control_rule(det, {"level": 1})
    assert is_viol is False

    # Level 2 test via evaluate_access_control_rule (sweeper in vault room restricted pool)
    is_viol, label = evaluate_access_control_rule(det, {"level": 2, "restricted_pools": ["cleaning_staff"]})
    assert is_viol is True
    assert label == LEVEL_2_LABEL


def test_evaluator_dispatcher_integration():
    det = {
        "label": "person",
        "score": 0.9,
        "box": {"xmin": 0, "ymin": 0, "xmax": 100, "ymax": 100},
        "identity_info": {"employeeId": None, "isUnauthorized": True}
    }
    rule_config = {
        "type": "access_control",
        "level": 1
    }
    # _passes_rule_config returns True if a violation is detected
    passes = _passes_rule_config(det, rule_config, frame_size=(1920, 1080))
    assert passes is True


if __name__ == "__main__":
    test_supported_rule_types_includes_access_control()
    test_level_1_outsider_detection()
    test_level_1_authorized_employee()
    test_level_2_outsider_rejected()
    test_level_2_restricted_employee_ids()
    test_level_2_restricted_pools()
    test_level_2_allowed_employee_ids_whitelist()
    test_level_2_allowed_pools_whitelist()
    test_access_control_rule_dispatcher()
    test_evaluator_dispatcher_integration()
    print("ALL ACCESS CONTROL UNIT TESTS PASSED SUCCESSFULLY!")

