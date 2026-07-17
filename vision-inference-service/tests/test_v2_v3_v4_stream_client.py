"""
V2 / V3 / V4 acceptance tests — rtsp/stream_client.py.

  V2 — drain-path selection: rtsp:// sources ALWAYS drain, regardless of
       SIMULATE_REALTIME_PLAYBACK; file sources honour the flag.
  V3 — reconnect: max_reconnect_attempts <= 0 retries forever with backoff
       capped at 60s; positive values keep the legacy give-up behaviour.
  V4 — OPENCV_FFMPEG_CAPTURE_OPTIONS carries an FFmpeg socket timeout.
"""
from tests._stubs import install_stubs

install_stubs()

import os  # noqa: E402
import threading  # noqa: E402

import config  # noqa: E402
import rtsp.stream_client as sc  # noqa: E402
from rtsp.models import CameraConfig  # noqa: E402


# ─────────────────────────────────────────────────────────────────────────────
# V4 — socket timeout present in FFmpeg capture options
# ─────────────────────────────────────────────────────────────────────────────

def test_ffmpeg_capture_options_include_socket_timeout():
    opts = os.environ.get("OPENCV_FFMPEG_CAPTURE_OPTIONS", "")
    assert "rtsp_transport;tcp" in opts
    assert "stimeout;" in opts
    # default 5s = 5_000_000 µs
    us = int(opts.split("stimeout;")[1].split("|")[0])
    assert us == int(float(config.RTSP_SOCKET_TIMEOUT_SECONDS) * 1_000_000)


# ─────────────────────────────────────────────────────────────────────────────
# V2 — drain-path selection helper
# ─────────────────────────────────────────────────────────────────────────────

def test_rtsp_url_always_drains_even_in_simulation_mode(monkeypatch):
    monkeypatch.setattr(sc.config, "SIMULATE_REALTIME_PLAYBACK", True)
    assert sc._use_live_drain_path("rtsp://cam.local/stream") is True
    assert sc._use_live_drain_path("RTSP://CAM.LOCAL/STREAM") is True  # case-insensitive
    assert sc._use_live_drain_path("  rtsp://cam.local/s ") is True   # whitespace tolerant
    assert sc._use_live_drain_path("rtsps://cam.local/stream") is True


def test_file_source_uses_simulation_branch_when_flag_on(monkeypatch):
    monkeypatch.setattr(sc.config, "SIMULATE_REALTIME_PLAYBACK", True)
    assert sc._use_live_drain_path("/videos/test.mp4") is False
    assert sc._use_live_drain_path("file:///videos/test.mp4") is False


def test_file_source_drains_when_flag_off(monkeypatch):
    monkeypatch.setattr(sc.config, "SIMULATE_REALTIME_PLAYBACK", False)
    assert sc._use_live_drain_path("/videos/test.mp4") is True
    assert sc._use_live_drain_path("rtsp://cam.local/stream") is True


# ─────────────────────────────────────────────────────────────────────────────
# V3 — reconnect exhaustion / infinite retry
# ─────────────────────────────────────────────────────────────────────────────

class _NeverOpensCap:
    def __init__(self, *a, **k):
        pass

    def isOpened(self):
        return False

    def release(self):
        pass

    def set(self, *a):
        pass


def _make_client(max_attempts: int) -> sc.RtspStreamClient:
    cfg = CameraConfig(
        camera_db_id="db-1",
        camera_id="CAM-T",
        tenant_id="t-1",
        tenant_name="tenant",
        rtsp_url="rtsp://example.local/stream",
    )
    client = sc.RtspStreamClient(config=cfg, frame_callback=lambda f, c: None)
    client._state.max_reconnect_attempts = max_attempts
    return client


def _run_connect(monkeypatch, max_attempts: int, stop_after_waits: int):
    """Drive _connect() against a camera that never opens.

    Returns (result, recorded_backoff_delays, final_status).
    """
    client = _make_client(max_attempts)
    monkeypatch.setattr(sc.cv2, "VideoCapture", _NeverOpensCap)
    monkeypatch.setattr(sc.time, "sleep", lambda s: None)  # skip the 1s negotiate pause

    waits = []
    stop_event = client._stop_event

    def fake_wait(timeout=None):
        waits.append(timeout)
        if len(waits) >= stop_after_waits:
            stop_event.set()
        return stop_event.is_set()

    monkeypatch.setattr(client._stop_event, "wait", fake_wait)

    result = client._connect()
    with client._state_lock:
        status = client._state.status
    return result, waits, status


def test_default_state_is_infinite_retries():
    client = _make_client(0)
    assert client._state.max_reconnect_attempts <= 0  # dataclass default is now 0


def test_infinite_retry_never_enters_error_state(monkeypatch):
    # Well past the legacy 10-attempt cap: 25 backoff waits = 26 attempts.
    result, waits, status = _run_connect(monkeypatch, max_attempts=0, stop_after_waits=25)
    assert result is None            # stopped via stop_event, not exhaustion
    assert status != "error"         # never gives up permanently
    assert len(waits) == 25          # kept retrying past 10 attempts


def test_infinite_retry_backoff_is_capped_at_60s(monkeypatch):
    _, waits, _ = _run_connect(monkeypatch, max_attempts=0, stop_after_waits=25)
    assert max(waits) == sc.RtspStreamClient.RECONNECT_MAX_DELAY == 60.0
    assert all(w <= 60.0 for w in waits)
    # backoff actually grows before hitting the cap: 2, 4, 8, ...
    assert waits[0] == 2.0
    assert waits[1] == 4.0
    assert waits[2] == 8.0


def test_finite_cap_still_enters_error_state(monkeypatch):
    """Backward compat: a positive max_reconnect_attempts keeps legacy semantics."""
    result, waits, status = _run_connect(monkeypatch, max_attempts=3, stop_after_waits=100)
    assert result is None
    assert status == "error"
    assert len(waits) == 2  # attempts 1..3, backoff waits between them only


def test_stop_event_interrupts_infinite_retry(monkeypatch):
    """stop() must be able to end an infinite retry loop promptly."""
    result, waits, status = _run_connect(monkeypatch, max_attempts=0, stop_after_waits=1)
    assert result is None
    assert len(waits) == 1
    assert status != "error"


# ─────────────────────────────────────────────────────────────────────────────
# FFmpeg stderr race — thread closure binds the Popen locally
# ─────────────────────────────────────────────────────────────────────────────

def test_log_stderr_thread_survives_ffmpeg_process_reset(monkeypatch):
    """The stderr reader must keep a local reference to the Popen so
    _stop_ffmpeg() nulling self._ffmpeg_process cannot crash it."""
    client = _make_client(0)
    client._config.is_streaming = True
    client._config.whip_url = "https://live.cloudflare.com/whip/abc"
    monkeypatch.setattr(sc.config, "CLOUDFLARE_API_TOKEN", "tok-123", raising=False)
    monkeypatch.setattr(sc.config, "CLOUDFLARE_WHIP_ALLOWED_HOSTS", ("cloudflare.com",), raising=False)

    started = threading.Event()
    release_stderr = threading.Event()

    class _FakeStderr:
        def __iter__(self):
            started.set()
            # Simulate ffmpeg holding the pipe open while the main thread
            # nulls self._ffmpeg_process.
            release_stderr.wait(timeout=5)
            return iter([b"frame drop\n"])

    class _FakePopen:
        def __init__(self, *a, **k):
            self.stderr = _FakeStderr()
            self.pid = 4242

        def poll(self):
            return 0  # already exited → _stop_ffmpeg skips terminate()

        def terminate(self):
            pass

        def wait(self, timeout=None):
            return 0

        def kill(self):
            pass

    monkeypatch.setattr(sc.subprocess, "Popen", _FakePopen)

    client._start_ffmpeg()
    assert started.wait(timeout=5), "stderr reader thread never started"

    # Race: drop the instance reference while the reader thread is mid-iteration
    client._stop_ffmpeg()
    assert client._ffmpeg_process is None
    release_stderr.set()
    # Nothing to assert beyond "no exception": before the fix the thread read
    # self._ffmpeg_process.stderr and raised AttributeError here.
