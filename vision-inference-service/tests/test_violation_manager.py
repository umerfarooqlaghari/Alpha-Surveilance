import pytest
import asyncio
from rtsp.violation_manager import ViolationManager

@pytest.fixture
def violation_manager():
    return ViolationManager(enter_buffer=3, exit_buffer=5)

@pytest.mark.asyncio
async def test_enter_buffer_debouncing(violation_manager):
    # Simulate single detection, should just move to PENDING
    detections = [{"label": "person", "score": 0.85, "box": {"xmin": 0, "ymin": 0, "xmax": 100, "ymax": 100}}]
    rules = [{"model_identifier": "human-detection", "trigger_labels": ["person"], "cooldown": 60}]
    
    actions = await violation_manager.process_frame("cam-test", detections, rules)
    assert len(actions) == 0, "Should not emit violation on frame 1 due to enter_buffer"

    # Push 2 more consecutive frames
    await violation_manager.process_frame("cam-test", detections, rules)
    actions = await violation_manager.process_frame("cam-test", detections, rules)
    
    # On the 3rd frame, it should trigger "New" state
    assert len(actions) == 1
    assert actions[0]["StateStatus"] == "New"
    
@pytest.mark.asyncio
async def test_cooldown_activation(violation_manager):
    # Fast track a violation into ACTIVE state
    detections = [{"label": "person", "score": 0.85, "box": {"xmin": 0, "ymin": 0, "xmax": 10, "ymax": 10}}]
    rules = [{"model_identifier": "human-detection", "trigger_labels": ["person"], "cooldown": 60}]
    
    await violation_manager.process_frame("cam-test", detections, rules)
    await violation_manager.process_frame("cam-test", detections, rules)
    actions = await violation_manager.process_frame("cam-test", detections, rules)
    assert actions[0]["StateStatus"] == "New"
    track_id = actions[0]["TrackId"]

    # Now drop the detections (object leaves frame)
    for _ in range(5): # exit_buffer is 5
        drop_actions = await violation_manager.process_frame("cam-test", [], rules)
    
    # 5 empty frames hit exit buffer... state degrades to COOLDOWN.
    # No actions returned during downward state shift.
    assert len(drop_actions) == 0
    state = violation_manager._get_state("cam-test", track_id)
    assert state.status.name == "COOLDOWN", "Manager failed to enter Cooldown phase"
