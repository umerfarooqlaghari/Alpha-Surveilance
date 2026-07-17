"""
Leak-fix acceptance test — main.on_frame bounded wait on process_frame.

A stalled ViolationManager / event loop used to freeze the capture thread
forever on ``future.result()``. Now the wait is bounded by
FRAME_DECISION_TIMEOUT_SECONDS and the frame is dropped with a warning.
"""
from tests._stubs import install_stubs

install_stubs()

import asyncio  # noqa: E402
import logging  # noqa: E402
import threading  # noqa: E402
import time  # noqa: E402

import numpy as np  # noqa: E402
from unittest.mock import MagicMock  # noqa: E402

import main  # noqa: E402
from rtsp.models import CameraConfig, ViolationRule  # noqa: E402


def _cam() -> CameraConfig:
    return CameraConfig(
        camera_db_id="db-1",
        camera_id="CAM-TIMEOUT",
        tenant_id="t-1",
        tenant_name="tenant",
        rtsp_url="rtsp://example/stream",
        violation_rules=[
            ViolationRule(sop_violation_type_id="sop-1", model_identifier="m-1")
        ],
    )


class _LoopThread:
    def __init__(self):
        self.loop = asyncio.new_event_loop()
        self.thread = threading.Thread(target=self._run, daemon=True)
        self.thread.start()

    def _run(self):
        asyncio.set_event_loop(self.loop)
        self.loop.run_forever()

    def close(self):
        self.loop.call_soon_threadsafe(self.loop.stop)
        self.thread.join(timeout=10)


def _wire(monkeypatch, vm, loop):
    monkeypatch.setattr(main, "violation_manager", vm)
    monkeypatch.setattr(main, "main_loop", loop)
    monkeypatch.setattr(main, "api_client", MagicMock())
    monkeypatch.setattr(main, "_streams_paused", False)
    monkeypatch.setattr(main.inference_engine, "run_inference", lambda *a, **k: [])
    monkeypatch.setattr(main, "evaluate_violations", lambda *a, **k: [])


def test_on_frame_drops_frame_when_state_decision_stalls(monkeypatch, caplog):
    lt = _LoopThread()
    try:
        vm = MagicMock()

        async def _stalls(*a, **k):
            await asyncio.sleep(30)
            return []

        vm.process_frame = _stalls
        vm.tag_tracks = MagicMock()

        _wire(monkeypatch, vm, lt.loop)
        monkeypatch.setattr(main.config, "FRAME_DECISION_TIMEOUT_SECONDS", 0.2)

        frame = np.zeros((8, 8, 3), dtype=np.uint8)
        start = time.monotonic()
        with caplog.at_level(logging.WARNING, logger="vision-service"):
            main.on_frame(frame, _cam())  # must return promptly, not raise
        elapsed = time.monotonic() - start

        assert elapsed < 5.0, f"on_frame blocked for {elapsed:.1f}s — timeout not applied"
        assert "timed out" in caplog.text
    finally:
        lt.close()


def test_on_frame_completes_normally_when_decision_is_fast(monkeypatch, caplog):
    lt = _LoopThread()
    try:
        vm = MagicMock()

        async def _fast(*a, **k):
            return []

        vm.process_frame = _fast
        vm.tag_tracks = MagicMock()

        _wire(monkeypatch, vm, lt.loop)
        monkeypatch.setattr(main.config, "FRAME_DECISION_TIMEOUT_SECONDS", 5.0)

        frame = np.zeros((8, 8, 3), dtype=np.uint8)
        with caplog.at_level(logging.WARNING, logger="vision-service"):
            main.on_frame(frame, _cam())

        assert "timed out" not in caplog.text
        assert "on_frame error" not in caplog.text
    finally:
        lt.close()
