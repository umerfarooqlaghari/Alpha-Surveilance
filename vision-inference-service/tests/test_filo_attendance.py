"""
tests/test_filo_attendance.py

Unit tests for CameraConfig attendance_mode field and ViolationApiClient attendance event dispatch.
"""

import pytest
import asyncio
from unittest.mock import AsyncMock, MagicMock
from rtsp.models import CameraConfig
from rtsp.violation_api_client import ViolationApiClient


def test_camera_config_attendance_mode():
    cam1 = CameraConfig(
        camera_db_id="cam-001-db",
        camera_id="CAM-001",
        tenant_id="tenant-001",
        tenant_name="Tenant One",
        rtsp_url="rtsp://127.0.0.1:554/stream1",
        attendance_mode="MarkIn"
    )
    cam2 = CameraConfig(
        camera_db_id="cam-001-db",
        camera_id="CAM-001",
        tenant_id="tenant-001",
        tenant_name="Tenant One",
        rtsp_url="rtsp://127.0.0.1:554/stream1",
        attendance_mode="MarkIn"
    )
    cam3 = CameraConfig(
        camera_db_id="cam-001-db",
        camera_id="CAM-001",
        tenant_id="tenant-001",
        tenant_name="Tenant One",
        rtsp_url="rtsp://127.0.0.1:554/stream1",
        attendance_mode="MarkOut"
    )

    assert cam1.attendance_mode == "MarkIn"
    assert cam1 == cam2
    assert cam1 != cam3


@pytest.mark.asyncio
async def test_post_attendance_record_client(monkeypatch):
    client = ViolationApiClient(base_url="http://localhost:5000", api_key="test-key")
    client._dlq_stopping = True


    mock_post = AsyncMock()
    mock_post.return_value.status_code = 200
    monkeypatch.setattr(client._http, "post", mock_post)

    payload = {
        "TenantId": "00000000-0000-0000-0000-000000000001",
        "CameraId": "cam-gate-01",
        "EmployeeExternalId": "EMP-001",
        "TrackId": 101,
        "Timestamp": "2026-07-23T21:00:00Z",
        "Confidence": 0.98
    }

    success = await client.post_attendance_record(payload)

    assert success is True
    mock_post.assert_called_once()
    assert mock_post.call_args[0][0] == "http://localhost:5000/api/attendance/internal/record"

    client._dlq_stopping = True
    if client._dlq_task:
        client._dlq_task.cancel()
        try:
            await client._dlq_task
        except asyncio.CancelledError:
            pass
    await client._http.aclose()



if __name__ == "__main__":
    test_camera_config_attendance_mode()
    asyncio.run(test_post_attendance_record_client(MagicMock()))
    print("ALL VISION INFERENCE FILO ATTENDANCE TESTS PASSED!")
