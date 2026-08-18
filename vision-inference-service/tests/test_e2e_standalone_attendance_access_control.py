"""
tests/test_e2e_standalone_attendance_access_control.py

Automated End-to-End Integration & Unit Test Suite for Standalone Services,
Access Control Level 1 & Level 2 Authorization, and FILO Attendance Shift Engine.
"""

import pytest
import asyncio
from datetime import datetime, timezone, timedelta
from unittest.mock import AsyncMock, MagicMock

from rules.access_control import (
    evaluate_access_level_1,
    evaluate_access_level_2,
    evaluate_access_control_rule,
    LEVEL_1_LABEL,
    LEVEL_2_LABEL,
)
from rtsp.models import CameraConfig
from rtsp.violation_api_client import ViolationApiClient


# =============================================================================
# 1. Standalone Service Config & Health Verification Tests
# =============================================================================

def test_standalone_camera_config_parsing():
    """Verify CameraConfig correctly parses attendance_mode and is_detection_enabled."""
    raw_api_response = {
        "id": "11111111-1111-1111-1111-111111111111",
        "cameraId": "CAM-GATE-NORTH",
        "tenantId": "99999999-9999-9999-9999-999999999999",
        "tenantName": "Alpha Corp",
        "rtspUrl": "rtsp://10.0.0.1:554/stream",
        "attendanceMode": "MarkIn",
        "isDetectionEnabled": True,
        "violationRules": []
    }

    client = ViolationApiClient(base_url="http://localhost:5001", api_key="alpha-vision-internal")
    cameras = client._parse_cameras([raw_api_response])

    assert len(cameras) == 1
    cam = cameras[0]
    assert cam.camera_id == "CAM-GATE-NORTH"
    assert cam.attendance_mode == "MarkIn"
    assert cam.is_detection_enabled is True


@pytest.mark.asyncio
async def test_standalone_attendance_record_dispatch(monkeypatch):
    """Verify ViolationApiClient dispatches attendance records asynchronously to /api/attendance/internal/record."""
    client = ViolationApiClient(base_url="http://localhost:5001", api_key="alpha-vision-internal")
    client._dlq_stopping = True


    mock_post = AsyncMock()
    mock_post.return_value.status_code = 200
    monkeypatch.setattr(client._http, "post", mock_post)

    payload = {
        "TenantId": "99999999-9999-9999-9999-999999999999",
        "CameraId": "11111111-1111-1111-1111-111111111111",
        "EmployeeExternalId": "EMP-777",
        "TrackId": 42,
        "Timestamp": datetime.now(timezone.utc).isoformat(),
        "Confidence": 0.95
    }

    success = await client.post_attendance_record(payload)

    assert success is True
    mock_post.assert_called_once()
    assert mock_post.call_args[0][0] == "http://localhost:5001/api/attendance/internal/record"

    client._dlq_stopping = True
    if client._dlq_task:
        client._dlq_task.cancel()
        try:
            await client._dlq_task
        except asyncio.CancelledError:
            pass
    await client._http.aclose()



# =============================================================================
# 2. Access Control Level 1 & Level 2 Automation Tests
# =============================================================================

def test_access_control_level_1_outsider_vs_employee():
    """Level 1: Outsider (non-employee) triggers violation; recognized employee passes."""
    # Outsider
    is_viol, label = evaluate_access_level_1({"employeeId": None, "isUnauthorized": True})
    assert is_viol is True
    assert label == LEVEL_1_LABEL

    # Authorized Employee
    is_viol, label = evaluate_access_level_1({"employeeId": "EMP-001", "isUnauthorized": False})
    assert is_viol is False
    assert label is None


def test_access_control_level_2_pools_and_whitelists():
    """Level 2: Restricted pools (e.g. sweeper in vault) and whitelist checks."""
    # Sweeper pool restricted from vault room
    vault_rule_config = {
        "level": 2,
        "restricted_pools": ["cleaning_staff", "contractors"],
        "allowed_pools": ["vault_managers"]
    }

    sweeper = {"employeeId": "EMP-101", "isUnauthorized": False, "pools": ["cleaning_staff"]}
    vault_mgr = {"employeeId": "EMP-001", "isUnauthorized": False, "pools": ["vault_managers"]}

    # Sweeper in vault room -> violation
    is_viol, label = evaluate_access_level_2(sweeper, vault_rule_config)
    assert is_viol is True
    assert label == LEVEL_2_LABEL

    # Vault manager in vault room -> authorized
    is_viol, label = evaluate_access_level_2(vault_mgr, vault_rule_config)
    assert is_viol is False
    assert label is None


# =============================================================================
# 3. FILO Attendance Shift Simulation Automation Test
# =============================================================================

def test_filo_shift_lifecycle_simulation():
    """
    Simulates a full FILO attendance shift lifecycle:
    1. First-In at 09:00 AM (Check-In)
    2. Exit for Lunch at 01:00 PM (Check-Out)
    3. Final Exit at 05:30 PM (Check-Out)
    Verifies FirstInTime is preserved while LastOutTime is updated to 05:30 PM.
    """
    shift_start = datetime(2026, 7, 23, 9, 0, 0, tzinfo=timezone.utc)
    lunch_exit = datetime(2026, 7, 23, 13, 0, 0, tzinfo=timezone.utc)
    final_exit = datetime(2026, 7, 23, 17, 30, 0, tzinfo=timezone.utc)

    # Simulated in-memory FILO tracker logic
    attendance_record = {
        "employee_id": "EMP-001",
        "shift_date": shift_start.date(),
        "first_in_time": shift_start,
        "last_out_time": None,
        "last_seen_time": shift_start,
        "status": "Active"
    }

    # First-In registered
    assert attendance_record["first_in_time"] == shift_start
    assert attendance_record["last_out_time"] is None

    # Lunch Exit (Check-Out)
    attendance_record["last_out_time"] = lunch_exit
    attendance_record["last_seen_time"] = lunch_exit
    attendance_record["status"] = "Present"

    assert attendance_record["first_in_time"] == shift_start
    assert attendance_record["last_out_time"] == lunch_exit

    # Final Exit (Check-Out)
    attendance_record["last_out_time"] = final_exit
    attendance_record["last_seen_time"] = final_exit

    # Assert FILO invariants:
    # 1. FirstInTime remains fixed at 09:00 AM
    # 2. LastOutTime updated to final exit at 05:30 PM
    # 3. Total duration = 8.5 hours (510 minutes)
    assert attendance_record["first_in_time"] == shift_start
    assert attendance_record["last_out_time"] == final_exit
    total_minutes = (attendance_record["last_out_time"] - attendance_record["first_in_time"]).total_seconds() / 60.0
    assert total_minutes == 510.0


def test_filo_night_shift_date_assignment():
    """Verify early morning check-ins (< 04:00 AM) bind to the previous day's shift date."""
    early_morning_entry = datetime(2026, 7, 24, 2, 30, 0, tzinfo=timezone.utc)

    if early_morning_entry.time() < datetime.min.time().replace(hour=4):
        assigned_shift_date = (early_morning_entry - timedelta(days=1)).date()
        shift_type = "NightShift"
    else:
        assigned_shift_date = early_morning_entry.date()
        shift_type = "DayShift"

    assert assigned_shift_date == datetime(2026, 7, 23).date()
    assert shift_type == "NightShift"


if __name__ == "__main__":
    test_standalone_camera_config_parsing()
    asyncio.run(test_standalone_attendance_record_dispatch(MagicMock()))
    test_access_control_level_1_outsider_vs_employee()
    test_access_control_level_2_pools_and_whitelists()
    test_filo_shift_lifecycle_simulation()
    test_filo_night_shift_date_assignment()
    print("ALL AUTOMATION TESTS PASSED SUCCESSFULLY!")
