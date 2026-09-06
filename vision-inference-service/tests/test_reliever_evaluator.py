"""
tests/test_reliever_evaluator.py

Unit tests for rules/reliever.py (Factory Worker Reliever System).
"""

import time
import pytest
from rules.reliever import evaluate_reliever_rule, check_unattended_workstations, _RELIEVER_STORE


@pytest.fixture(autouse=True)
def clear_store():
    _RELIEVER_STORE.clear()


def test_person_inside_and_outside_polygon():
    rule_config = {
        "type": "reliever",
        "polygon": [[0.2, 0.2], [0.8, 0.2], [0.8, 0.8], [0.2, 0.8]],
        "coordinate_space": "normalized",
        "anchor": "bottom_center",
        "handover_threshold_s": 30.0,
        "max_relief_duration_s": 300.0,
        "required_primaries": 1,
    }

    # Frame size 1000x1000
    frame_size = (1000, 1000)

    # 1. Person outside polygon (bottom-center at x=100, y=100 -> norm 0.1, 0.1)
    outside_det = {
        "box": {"xmin": 50, "ymin": 50, "xmax": 150, "ymax": 100},
        "track_id": "track_1",
        "label": "person",
    }
    is_viol, label, _ = evaluate_reliever_rule(outside_det, rule_config, frame_size=frame_size, camera_id="cam1")
    assert not is_viol

    # 2. Primary worker inside polygon (bottom-center at x=500, y=500 -> norm 0.5, 0.5)
    inside_primary_det = {
        "box": {"xmin": 450, "ymin": 450, "xmax": 550, "ymax": 500},
        "track_id": "track_primary_1",
        "label": "person",
        "identity_info": {"employeeId": "EMP-001", "role": "Primary Operator"},
    }
    is_viol, label, _ = evaluate_reliever_rule(inside_primary_det, rule_config, frame_size=frame_size, camera_id="cam1")
    assert not is_viol


def test_handover_and_unattended_timeout():
    rule_config = {
        "name": "Line 1 Station A",
        "type": "reliever",
        "polygon": [[0.1, 0.1], [0.9, 0.1], [0.9, 0.9], [0.1, 0.9]],
        "coordinate_space": "normalized",
        "anchor": "bottom_center",
        "handover_threshold_s": 0.5, # 0.5s for fast unit test
        "max_relief_duration_s": 2.0,
        "required_primaries": 1,
        "reliever_employee_ids": ["REL-100"]
    }
    frame_size = (1000, 1000)

    # 1. Primary worker enters
    primary_det = {
        "box": {"xmin": 400, "ymin": 400, "xmax": 600, "ymax": 500},
        "track_id": "track_p1",
        "identity_info": {"employeeId": "EMP-001", "role": "Primary"}
    }
    evaluate_reliever_rule(primary_det, rule_config, frame_size=frame_size, camera_id="cam1")
    
    # Check end of frame 1 - primary present, no violation
    violations = check_unattended_workstations("cam1", [rule_config])
    assert len(violations) == 0

    # 2. Frame 2: Primary worker left (nobody in polygon) -> starts grace timer
    violations = check_unattended_workstations("cam1", [rule_config])
    assert len(violations) == 0 # Still within grace period

    # Wait for threshold to expire (0.6s)
    time.sleep(0.6)

    # Frame 3: Threshold expired with no reliever
    violations = check_unattended_workstations("cam1", [rule_config])
    assert len(violations) == 1
    assert violations[0]["violation_type"] == "unattended_workstation"
    assert violations[0]["elapsed_unattended_s"] >= 0.5


def test_reliever_takeover_and_overdue_alert():
    rule_config = {
        "name": "Line 1 Station B",
        "type": "reliever",
        "polygon": [[0.1, 0.1], [0.9, 0.1], [0.9, 0.9], [0.1, 0.9]],
        "coordinate_space": "normalized",
        "handover_threshold_s": 2.0,
        "max_relief_duration_s": 0.4, # 0.4s max relief for fast test
        "reliever_employee_ids": ["REL-200"]
    }
    frame_size = (1000, 1000)

    # 1. Reliever steps in to cover
    reliever_det = {
        "box": {"xmin": 400, "ymin": 400, "xmax": 600, "ymax": 500},
        "track_id": "track_r1",
        "identity_info": {"employeeId": "REL-200", "role": "Reliever"}
    }
    is_viol, label, meta = evaluate_reliever_rule(reliever_det, rule_config, frame_size=frame_size, camera_id="cam1")
    assert not is_viol # On initial arrival, within limit

    # Sleep past max relief duration
    time.sleep(0.5)

    # Next frame with reliever still present past max limit
    is_viol, label, meta = evaluate_reliever_rule(reliever_det, rule_config, frame_size=frame_size, camera_id="cam1")
    assert is_viol
    assert label == "overdue_relief_duration"
    assert meta["workstation_state"] == "OVERDUE_ALERT"


def test_simultaneous_sop_and_reliever_camera_configuration():
    from rules.evaluator import evaluate_violations
    from rules.reliever import pop_pending_relief_events

    ppe_rule = {
        "name": "Hairnet Enforcement",
        "sop_violation_type_id": "sop-hairnet-001",
        "model_identifier": "restaurant-ppe-v1",
        "trigger_labels": ["no-hairnet"],
        "confidence_threshold": 0.5,
        "rule_config": {
            "type": "geofence",
            "polygon": [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]],
            "coordinate_space": "normalized",
        }
    }

    reliever_rule = {
        "name": "Assembly Line Workstation 1",
        "sop_violation_type_id": "ws-001",
        "model_identifier": "human-detection-v1",
        "trigger_labels": ["person"],
        "confidence_threshold": 0.5,
        "rule_config": {
            "type": "reliever",
            "workstation_id": "ws-001",
            "polygon": [[0.2, 0.2], [0.8, 0.2], [0.8, 0.8], [0.2, 0.8]],
            "coordinate_space": "normalized",
            "handover_threshold_s": 0.5,
            "max_relief_duration_s": 600.0,
            "required_primaries": 1,
        }
    }

    rules = [ppe_rule, reliever_rule]
    frame_size = (1000, 1000)

    # Frame 1: Person inside workstation, detected without hairnet
    # -> Should emit PPE violation ("no-hairnet")
    # -> Reliever rule should track presence and NOT emit any violation!
    det_person = {
        "box": {"xmin": 400, "ymin": 400, "xmax": 600, "ymax": 500},
        "score": 0.95,
        "label": "person",
        "source_model": "human-detection-v1",
        "track_id": "track_101",
        "identity_info": {"employeeId": "EMP-001", "role": "Primary Operator"}
    }
    det_ppe = {
        "box": {"xmin": 450, "ymin": 410, "xmax": 550, "ymax": 480},
        "score": 0.92,
        "label": "no-hairnet",
        "raw_label": "no-hairnet",
        "source_model": "restaurant-ppe-v1",
    }

    v1 = evaluate_violations([det_person, det_ppe], rules, frame_size=frame_size, camera_id="cam_simul_1")
    assert len(v1) == 1
    assert v1[0]["label"] == "no-hairnet"
    assert v1[0]["matched_rule"] == "Hairnet Enforcement"

    # Frame 2: Person leaves workstation (empty frame)
    # -> Grace timer starts
    time.sleep(0.1)
    v2 = evaluate_violations([], rules, frame_size=frame_size, camera_id="cam_simul_1")
    assert len(v2) == 0

    # Pop events to check PRIMARY_EXIT was enqueued
    events = pop_pending_relief_events()
    assert any(e["EventType"] == "PRIMARY_EXIT" for e in events)

    # Wait for grace period (0.5s) to expire
    time.sleep(0.5)

    # Frame 3: Grace period expired with no reliever stationed
    # -> Unattended workstation violation emitted!
    v3 = evaluate_violations([], rules, frame_size=frame_size, camera_id="cam_simul_1")
    assert len(v3) == 1
    assert v3[0]["violation_type"] == "unattended_workstation"
    assert v3[0]["matched_rule"] == "Assembly Line Workstation 1"

    events_after = pop_pending_relief_events()
    assert any(e["EventType"] == "UNATTENDED_TIMEOUT" for e in events_after)


def test_is_within_operating_hours():
    from datetime import datetime, timezone
    from rules.reliever import is_within_operating_hours

    # 1. No schedule -> always True (24/7)
    assert is_within_operating_hours({}) is True

    # 2. Daytime shift: Mon-Fri 08:00 to 16:00 (days 1..5)
    sched_daytime = {
        "operating_schedules": [
            {
                "id": "w1",
                "label": "Morning Shift",
                "startTime": "08:00",
                "endTime": "16:00",
                "daysOfWeek": [1, 2, 3, 4, 5],
                "isActive": True,
            }
        ]
    }

    # Monday 2026-09-07 10:30 UTC -> inside
    mon_1030 = datetime(2026, 9, 7, 10, 30, tzinfo=timezone.utc)
    assert is_within_operating_hours(sched_daytime, mon_1030) is True

    # Monday 2026-09-07 16:05 UTC -> outside
    mon_1605 = datetime(2026, 9, 7, 16, 5, tzinfo=timezone.utc)
    assert is_within_operating_hours(sched_daytime, mon_1605) is False

    # Sunday 2026-09-06 10:30 UTC -> day not in schedule
    sun_1030 = datetime(2026, 9, 6, 10, 30, tzinfo=timezone.utc)
    assert is_within_operating_hours(sched_daytime, sun_1030) is False

    # 3. Overnight shift: Monday 22:00 to 06:00 (day 1)
    sched_overnight = {
        "operating_schedules": [
            {
                "id": "w2",
                "label": "Overnight Shift",
                "startTime": "22:00",
                "endTime": "06:00",
                "daysOfWeek": [1],
                "isActive": True,
            }
        ]
    }

    # Monday 23:30 -> inside evening portion
    mon_2330 = datetime(2026, 9, 7, 23, 30, tzinfo=timezone.utc)
    assert is_within_operating_hours(sched_overnight, mon_2330) is True

    # Tuesday 04:00 -> inside morning portion (spillover from Monday night)
    tue_0400 = datetime(2026, 9, 8, 4, 0, tzinfo=timezone.utc)
    assert is_within_operating_hours(sched_overnight, tue_0400) is True

    # Tuesday 07:00 -> outside
    tue_0700 = datetime(2026, 9, 8, 7, 0, tzinfo=timezone.utc)
    assert is_within_operating_hours(sched_overnight, tue_0700) is False


def test_off_duty_suppresses_unattended_violation():
    """Verify that when a workstation is outside operating hours, no unattended alarms trigger."""
    # Active window configured for a time that is definitely NOT now (e.g. 02:00 to 03:00)
    rule_config = {
        "name": "Line 1 Station Off Duty",
        "type": "reliever",
        "polygon": [[0.1, 0.1], [0.9, 0.1], [0.9, 0.9], [0.1, 0.9]],
        "coordinate_space": "normalized",
        "handover_threshold_s": 0.1,
        "max_relief_duration_s": 2.0,
        "operating_schedules": [
            {
                "id": "w_closed",
                "label": "Closed Window",
                "startTime": "00:01",
                "endTime": "00:02",
                "daysOfWeek": [0], # Sunday only at 00:01-00:02
                "isActive": True,
            }
        ]
    }

    # Empty frame, workstation is unoccupied
    violations = check_unattended_workstations("cam_closed", [rule_config])
    assert len(violations) == 0

    # Even after sleeping longer than handover_threshold_s, off-duty hours suppress the alarm
    time.sleep(0.2)
    violations2 = check_unattended_workstations("cam_closed", [rule_config])
    assert len(violations2) == 0


def test_shift_specific_worker_roster():
    """Verify that reliever identification prioritizes the active shift's specific roster."""
    from datetime import datetime, timezone
    from rules.reliever import _is_reliever_person, get_active_operating_shift

    rule_config = {
        "operating_schedules": [
            {
                "id": "shift-1",
                "label": "Morning Shift",
                "startTime": "08:00",
                "endTime": "16:00",
                "daysOfWeek": [1, 2, 3, 4, 5],
                "isActive": True,
                "primary_employee_ids": ["EMP-MORNING-1"],
                "reliever_employee_ids": ["REL-MORNING-1"]
            },
            {
                "id": "shift-2",
                "label": "Evening Shift",
                "startTime": "16:00",
                "endTime": "00:00",
                "daysOfWeek": [1, 2, 3, 4, 5],
                "isActive": True,
                "primary_employee_ids": ["EMP-EVENING-1"],
                "reliever_employee_ids": ["REL-EVENING-1"]
            }
        ],
        "reliever_employee_ids": ["REL-GLOBAL-FALLBACK"]
    }

    # Time 1: Monday at 10:00 UTC (Shift 1 active)
    mon_morning = datetime(2026, 9, 7, 10, 0, tzinfo=timezone.utc)
    active_shift_1 = get_active_operating_shift(rule_config, mon_morning)
    assert active_shift_1 is not None
    assert active_shift_1["id"] == "shift-1"

    det_morning_reliever = {"identity_info": {"employeeId": "REL-MORNING-1"}}
    det_evening_reliever = {"identity_info": {"employeeId": "REL-EVENING-1"}}

    assert _is_reliever_person(det_morning_reliever, rule_config, active_shift=active_shift_1) is True
    assert _is_reliever_person(det_evening_reliever, rule_config, active_shift=active_shift_1) is False

    # Time 2: Monday at 18:00 UTC (Shift 2 active)
    mon_evening = datetime(2026, 9, 7, 18, 0, tzinfo=timezone.utc)
    active_shift_2 = get_active_operating_shift(rule_config, mon_evening)
    assert active_shift_2 is not None
    assert active_shift_2["id"] == "shift-2"

    assert _is_reliever_person(det_morning_reliever, rule_config, active_shift=active_shift_2) is False
    assert _is_reliever_person(det_evening_reliever, rule_config, active_shift=active_shift_2) is True



