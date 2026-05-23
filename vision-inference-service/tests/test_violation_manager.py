from types import SimpleNamespace
import asyncio

import pytest

from rtsp.violation_manager import ViolationManager


@pytest.fixture
def violation_manager():
    return ViolationManager(entry_hysteresis=3, exit_buffer=5)


def _rule():
    return SimpleNamespace(
        sop_violation_type_id="sop-person",
        model_identifier="human-detection-v1",
        trigger_labels=["person"],
    )


def _person_detection():
    return {
        "label": "person",
        "score": 0.85,
        "box": {"xmin": 0, "ymin": 0, "xmax": 100, "ymax": 100},
        "source_model": "human-detection-v1",
    }


def test_enter_buffer_debouncing(violation_manager):
    asyncio.run(_test_enter_buffer_debouncing(violation_manager))


async def _test_enter_buffer_debouncing(violation_manager):
    detections = [_person_detection()]
    rules = [_rule()]

    actions = await violation_manager.process_frame("cam-test", detections, rules)
    assert len(actions) == 0

    await violation_manager.process_frame("cam-test", detections, rules)
    actions = await violation_manager.process_frame("cam-test", detections, rules)

    assert len(actions) == 1
    assert actions[0]["StateStatus"] == "New"


def test_cooldown_activation(violation_manager):
    asyncio.run(_test_cooldown_activation(violation_manager))


async def _test_cooldown_activation(violation_manager):
    detections = [_person_detection()]
    rules = [_rule()]

    await violation_manager.process_frame("cam-test", detections, rules)
    await violation_manager.process_frame("cam-test", detections, rules)
    actions = await violation_manager.process_frame("cam-test", detections, rules)
    assert actions[0]["StateStatus"] == "New"

    for _ in range(5):
        drop_actions = await violation_manager.process_frame("cam-test", [], rules)

    assert len(drop_actions) == 0
    camera_states = violation_manager._states["cam-test"]
    assert any(state["state"] == violation_manager.STATE_COOLDOWN for state in camera_states.values())
