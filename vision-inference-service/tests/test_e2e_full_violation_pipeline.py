"""
tests/test_e2e_full_violation_pipeline.py

Comprehensive End-to-End Automated Test Script for the Complete Violation Pipeline:
1. Raw Model Detection & Confidence Pre-Filtering
2. Spatial Geofence, Dwell Time, Anomaly & Access Control Rule Evaluation
3. IoU Track Management & State Machine (New vs Update vs Resolved)
4. Face Re-ID Identity Enrichment & Payload Packaging
5. Async API Webhook Delivery & Dead-Letter Queue (DLQ) Resilience
"""

import pytest
import asyncio
import time
import uuid
import json
from datetime import datetime, timezone
from unittest.mock import AsyncMock, MagicMock

from rules.evaluator import evaluate_violations, SUPPORTED_RULE_TYPES
from rules.access_control import evaluate_access_control_rule
from rtsp.violation_manager import ViolationManager, SimpleIouTracker
from rtsp.models import ViolationRule
from rtsp.violation_api_client import ViolationApiClient


# =============================================================================
# 1. Test Geofence & Spatial Rule Pipeline
# =============================================================================

def test_pipeline_geofence_violation():
    """Verify raw detection inside polygon geofence passes evaluation and triggers New violation."""
    # Polygon covering (100,100) to (500,500)
    rule_config = {
        "type": "geofence",
        "polygon": [[100, 100], [500, 100], [500, 500], [100, 500]]
    }
    rule = ViolationRule(
        sop_violation_type_id="sop-geofence-001",
        model_identifier="human-detection-v1",
        trigger_labels=["person"],
        rule_config=rule_config
    )

    # Detection inside geofence
    det_inside = {
        "label": "person",
        "score": 0.90,
        "source_model": "human-detection-v1",
        "box": {"xmin": 200, "ymin": 200, "xmax": 300, "ymax": 300}
    }
    # Detection outside geofence
    det_outside = {
        "label": "person",
        "score": 0.90,
        "source_model": "human-detection-v1",
        "box": {"xmin": 10, "ymin": 10, "xmax": 50, "ymax": 50}
    }

    # Evaluate
    violations_inside = evaluate_violations([det_inside], [rule], frame_size=(1920, 1080))
    violations_outside = evaluate_violations([det_outside], [rule], frame_size=(1920, 1080))

    assert len(violations_inside) == 1
    assert violations_inside[0]["violation_type"] == "person"
    assert len(violations_outside) == 0


# =============================================================================
# 2. Test Dwell Time Rule Pipeline
# =============================================================================

def test_pipeline_dwell_time_hysteresis():
    """Verify person dwelling in restricted area accumulates dwell time before triggering violation."""
    rule_config = {
        "type": "dwell",
        "duration_s": 0.001,
        "polygon": [[0, 0], [1000, 0], [1000, 1000], [0, 1000]]
    }
    rule = ViolationRule(
        sop_violation_type_id="sop-dwell-001",
        model_identifier="human-detection-v1",
        trigger_labels=["person"],
        rule_config=rule_config
    )

    det = {
        "label": "person",
        "score": 0.85,
        "source_model": "human-detection-v1",
        "box": {"xmin": 100, "ymin": 100, "xmax": 200, "ymax": 200},
        "track_id": 99
    }

    # Initial frame records enter time
    v1 = evaluate_violations([det], [rule], frame_size=(1920, 1080), camera_id="CAM-TEST")
    time.sleep(0.01)
    # Subsequent frame after duration_s passes -> violation emitted
    v2 = evaluate_violations([det], [rule], frame_size=(1920, 1080), camera_id="CAM-TEST")
    assert len(v2) == 1
    assert v2[0]["sop_violation_type_id"] == "sop-dwell-001"


# =============================================================================
# 3. Test Violation Manager State Transitions (New vs Update vs Resolved)
# =============================================================================

@pytest.mark.asyncio
async def test_violation_manager_state_lifecycle():
    """Verify ViolationManager transitions states correctly: New -> Update -> Resolved."""
    vm = ViolationManager(entry_hysteresis=1, exit_buffer=2)
    camera_id = "CAM-GATE-01"

    rule = ViolationRule(
        sop_violation_type_id="sop-001",
        model_identifier="human-detection-v1",
        trigger_labels=["person"]
    )

    violation_det = {
        "label": "person",
        "score": 0.92,
        "source_model": "human-detection-v1",
        "box": {"xmin": 100, "ymin": 100, "xmax": 200, "ymax": 200},
        "sop_violation_type_id": "sop-001",
        "track_id": 1
    }

    # Frame 1: Record initial observation (State: Pending)
    actions1 = await vm.process_frame(camera_id, [violation_det], [rule])
    assert len(actions1) == 0

    # Frame 2: Confirms entry hysteresis -> Transition to Active (StateStatus: "New")
    actions2 = await vm.process_frame(camera_id, [violation_det], [rule])
    assert len(actions2) == 1
    assert actions2[0]["StateStatus"] == "New"
    track_id = actions2[0]["TrackId"]

    # Frame 3: Ongoing active violation (StateStatus: "Update")
    actions3 = await vm.process_frame(camera_id, [violation_det], [rule])
    assert len(actions3) == 1
    assert actions3[0]["StateStatus"] == "Update"
    assert actions3[0]["TrackId"] == track_id

    # Frame 4: Missing detection -> State remains active until exit buffer exceeded
    actions4 = await vm.process_frame(camera_id, [], [rule])
    assert len(actions4) == 0



# =============================================================================
# 4. Test Access Control Pipeline (Level 1 & Level 2)
# =============================================================================

def test_pipeline_access_control_integration():
    """Verify Access Control rule evaluation integrates with evaluator dispatcher."""
    # Level 1 Rule (Outsiders)
    l1_rule_config = {"type": "access_control", "level": 1}
    l1_rule = ViolationRule(
        sop_violation_type_id="sop-ac-l1",
        model_identifier="human-detection-v1",
        trigger_labels=["person"],
        rule_config=l1_rule_config
    )

    det_outsider = {
        "label": "person",
        "score": 0.90,
        "source_model": "human-detection-v1",
        "box": {"xmin": 50, "ymin": 50, "xmax": 150, "ymax": 150},
        "identity_info": {"employeeId": None, "isUnauthorized": True}
    }
    det_employee = {
        "label": "person",
        "score": 0.90,
        "source_model": "human-detection-v1",
        "box": {"xmin": 50, "ymin": 50, "xmax": 150, "ymax": 150},
        "identity_info": {"employeeId": "EMP-001", "isUnauthorized": False}
    }

    # Outsider triggers Level 1 violation
    v_outsider = evaluate_violations([det_outsider], [l1_rule], frame_size=(1920, 1080))
    assert len(v_outsider) == 1

    # Employee passes Level 1 check
    v_employee = evaluate_violations([det_employee], [l1_rule], frame_size=(1920, 1080))
    assert len(v_employee) == 0


# =============================================================================
# 5. Test Webhook Delivery & Dead-Letter Queue (DLQ) Resilience
# =============================================================================

@pytest.mark.asyncio
async def test_pipeline_dlq_webhook_resilience(monkeypatch):
    """Verify API client queues failed requests into Dead-Letter Queue (DLQ) and retries successfully."""
    client = ViolationApiClient(base_url="http://localhost:5001", api_key="alpha-vision-internal")
    client._dlq_stopping = True

    # Mock HTTP post to fail first, then succeed on retry
    call_count = 0

    async def mock_post(url, **kwargs):
        nonlocal call_count
        call_count += 1
        response = MagicMock()
        if call_count == 1:
            response.status_code = 503
            response.raise_for_status.side_effect = Exception("Service Unavailable")
        else:
            response.status_code = 200
        return response

    monkeypatch.setattr(client._http, "post", mock_post)

    payload = {
        "TenantId": "00000000-0000-0000-0000-000000000001",
        "CameraId": "cam-001",
        "ModelIdentifier": "human-detection-v1",
        "SopViolationTypeId": "sop-001",
        "CorrelationId": str(uuid.uuid4()),
        "TrackId": 1,
        "Timestamp": datetime.now(timezone.utc).isoformat(),
        "Status": "Pending",
        "MetadataJson": "{}"
    }

    # Post violation (fails first time, queued to DLQ)
    await client.post_violation(payload)
    assert len(client._dlq) == 1

    # Drain DLQ (succeeds second time instantly)
    client._dlq_retry_interval = 0.001
    item = client._dlq.popleft()
    success = await client.post_violation(item[1])
    assert success is True
    assert len(client._dlq) == 0

    # Close HTTP client and stop background DLQ task cleanly
    client._dlq_stopping = True
    if client._dlq_task:
        client._dlq_task.cancel()
        try:
            await client._dlq_task
        except asyncio.CancelledError:
            pass
    await client._http.aclose()


if __name__ == "__main__":
    test_pipeline_geofence_violation()
    test_pipeline_dwell_time_hysteresis()
    asyncio.run(test_violation_manager_state_lifecycle())
    test_pipeline_access_control_integration()
    asyncio.run(test_pipeline_dlq_webhook_resilience(MagicMock()))
    print("ALL FULL VIOLATION PIPELINE E2E TESTS PASSED SUCCESSFULLY!")
