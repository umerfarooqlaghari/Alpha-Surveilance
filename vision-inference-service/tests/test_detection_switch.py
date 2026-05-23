"""
tests/test_detection_switch.py
Tests for the IsDetectionEnabled detection switch feature and the
RTSP TEARDOWN best-effort helper.

Detection switch design
-----------------------
When IsDetectionEnabled is False the backend excludes the camera from
GET /api/cameras/internal/active, so the stream_manager reconcile loop
stops the RtspStreamClient (which now fires TEARDOWN before cap.release()).
These tests cover:
  1. CameraConfig default: is_detection_enabled = True
  2. Explicit False construction
  3. violation_api_client._parse_cameras reads "isDetectionEnabled" correctly
  4. Missing key in API response defaults to True (backwards-compat)
  5. _send_rtsp_teardown: sends TEARDOWN message over a real loopback socket
  6. _send_rtsp_teardown: swallows connection-refused exceptions
  7. _send_rtsp_teardown: swallows timeout exceptions
  8. _send_rtsp_teardown: handles malformed / no-port URL gracefully
"""

import socket
import threading
import time
import unittest
from unittest.mock import MagicMock, patch, call

from rtsp.models import CameraConfig, ViolationRule
from rtsp.stream_client import _send_rtsp_teardown


# ── 1-2: CameraConfig defaults ────────────────────────────────────────────────

class TestCameraConfigDetectionFlag:
    """CameraConfig.is_detection_enabled field."""

    def _make_config(self, **kwargs) -> CameraConfig:
        defaults = dict(
            camera_db_id="db-1",
            camera_id="CAM-01",
            tenant_id="t-1",
            tenant_name="Acme",
            rtsp_url="rtsp://192.168.1.10/stream",
        )
        defaults.update(kwargs)
        return CameraConfig(**defaults)

    def test_defaults_to_true(self):
        """is_detection_enabled must default to True (existing cameras unaffected)."""
        config = self._make_config()
        assert config.is_detection_enabled is True

    def test_explicit_false(self):
        """Explicitly setting False must be stored correctly."""
        config = self._make_config(is_detection_enabled=False)
        assert config.is_detection_enabled is False

    def test_explicit_true(self):
        config = self._make_config(is_detection_enabled=True)
        assert config.is_detection_enabled is True

    def test_flag_independent_of_other_fields(self):
        """Flag must not be influenced by is_streaming or any other field."""
        config = self._make_config(is_streaming=False, is_detection_enabled=True)
        assert config.is_detection_enabled is True

        config2 = self._make_config(is_streaming=True, is_detection_enabled=False)
        assert config2.is_detection_enabled is False

    def test_hash_not_affected(self):
        """__hash__ must remain based on camera_id regardless of detection flag."""
        c1 = self._make_config(is_detection_enabled=True)
        c2 = self._make_config(is_detection_enabled=False)
        assert hash(c1) == hash(c2), "hash must not vary by detection flag"


# ── 3-4: _parse_cameras reads the flag ───────────────────────────────────────

class TestViolationApiClientParseDetectionFlag:
    """
    violation_api_client._parse_cameras must map isDetectionEnabled
    from the API JSON payload onto CameraConfig.is_detection_enabled.
    """

    def _parse(self, items):
        """Call _parse_cameras on a lightweight stub client."""
        from rtsp.violation_api_client import ViolationApiClient
        client = ViolationApiClient.__new__(ViolationApiClient)
        # Provide just enough state for _parse_cameras to work.
        client._logger = MagicMock()
        return client._parse_cameras(items)

    def _base_item(self, **overrides):
        item = {
            "id": "db-uuid-1",
            "cameraId": "CAM-TEST",
            "tenantId": "t-uuid",
            "tenantName": "Acme",
            "rtspUrl": "rtsp://10.0.0.1/live",
            "whipUrl": "",
            "isStreaming": False,
            "isDetectionEnabled": True,
            "targetFps": 1.0,
            "name": "Lobby",
            "location": "Floor 1",
            "violationRules": [],
        }
        item.update(overrides)
        return item

    def test_parse_detection_enabled_true(self):
        configs = self._parse([self._base_item(isDetectionEnabled=True)])
        assert len(configs) == 1
        assert configs[0].is_detection_enabled is True

    def test_parse_detection_enabled_false(self):
        configs = self._parse([self._base_item(isDetectionEnabled=False)])
        assert len(configs) == 1
        assert configs[0].is_detection_enabled is False

    def test_parse_missing_key_defaults_true(self):
        """Backwards-compat: older API responses without the key default to True."""
        item = self._base_item()
        del item["isDetectionEnabled"]
        configs = self._parse([item])
        assert configs[0].is_detection_enabled is True

    def test_parse_null_value_defaults_true(self):
        """A null JSON value (None in Python) must also default to True."""
        configs = self._parse([self._base_item(isDetectionEnabled=None)])
        assert configs[0].is_detection_enabled is True

    def test_parse_multiple_cameras_flags_independent(self):
        items = [
            self._base_item(cameraId="A", isDetectionEnabled=True),
            self._base_item(cameraId="B", isDetectionEnabled=False),
            self._base_item(cameraId="C", isDetectionEnabled=True),
        ]
        configs = self._parse(items)
        by_id = {c.camera_id: c for c in configs}
        assert by_id["A"].is_detection_enabled is True
        assert by_id["B"].is_detection_enabled is False
        assert by_id["C"].is_detection_enabled is True


# ── 5-8: _send_rtsp_teardown ─────────────────────────────────────────────────

def _mock_socket():
    """Return a mock socket that behaves like a context-manager and times out on recv."""
    sock = MagicMock()
    sock.__enter__ = MagicMock(return_value=sock)
    sock.__exit__ = MagicMock(return_value=False)
    sock.recv.side_effect = socket.timeout
    return sock


class TestSendRtspTeardown:
    """_send_rtsp_teardown unit tests."""

    # ── 5: happy path (mock socket — avoids loopback timing races) ────────────
    def test_teardown_sends_correct_message(self):
        """TEARDOWN message must contain the correct verb, URI, and CSeq."""
        sock = _mock_socket()
        with patch("rtsp.stream_client.socket.create_connection", return_value=sock):
            _send_rtsp_teardown("rtsp://192.168.1.10:554/live/stream")

        sock.sendall.assert_called_once()
        sent = sock.sendall.call_args[0][0].decode("ascii")
        assert "TEARDOWN rtsp://192.168.1.10:554/live/stream RTSP/1.0" in sent
        assert "CSeq: 1" in sent
        assert "User-Agent:" in sent

    def test_teardown_uri_excludes_credentials(self):
        """Credentials in the RTSP URL must NOT appear in the TEARDOWN request line."""
        sock = _mock_socket()
        with patch("rtsp.stream_client.socket.create_connection", return_value=sock):
            _send_rtsp_teardown("rtsp://admin:secret@192.168.1.10:554/secure/stream")

        sent = sock.sendall.call_args[0][0].decode("ascii")
        assert "admin" not in sent, "credentials must be stripped from TEARDOWN URI"
        assert "secret" not in sent

    def test_teardown_default_port_554(self):
        """URL without explicit port must connect to port 554."""
        sock = _mock_socket()
        with patch("rtsp.stream_client.socket.create_connection", return_value=sock) as mock_cc:
            _send_rtsp_teardown("rtsp://192.168.1.10/stream")

        host, port = mock_cc.call_args[0][0]
        assert port == 554

    # ── 6: connection refused ─────────────────────────────────────────────────
    def test_teardown_swallows_connection_refused(self):
        """Must not raise even if no server is listening."""
        with patch(
            "rtsp.stream_client.socket.create_connection",
            side_effect=ConnectionRefusedError,
        ):
            _send_rtsp_teardown("rtsp://127.0.0.1:1/stream", timeout=0.3)  # must not raise

    # ── 7: timeout ────────────────────────────────────────────────────────────
    def test_teardown_swallows_timeout(self):
        """Must not raise on connect timeout."""
        with patch(
            "rtsp.stream_client.socket.create_connection",
            side_effect=socket.timeout,
        ):
            _send_rtsp_teardown("rtsp://192.0.2.1:554/stream", timeout=0.01)  # must not raise

    # ── 8: malformed URL ─────────────────────────────────────────────────────
    def test_teardown_handles_no_port_in_url(self):
        """URL without explicit port must not raise (defaults to 554)."""
        sock = _mock_socket()
        with patch("rtsp.stream_client.socket.create_connection", return_value=sock):
            _send_rtsp_teardown("rtsp://192.168.1.10/stream", timeout=0.3)  # must not raise
        sock.sendall.assert_called_once()  # socket was actually used

    def test_teardown_handles_empty_path(self):
        """URL with no path component must not raise."""
        with patch(
            "rtsp.stream_client.socket.create_connection",
            side_effect=ConnectionRefusedError,
        ):
            _send_rtsp_teardown("rtsp://127.0.0.1:1", timeout=0.3)  # must not raise

    def test_teardown_handles_totally_invalid_url(self):
        """Completely invalid URL must not raise (best-effort)."""
        _send_rtsp_teardown("not-a-url-at-all", timeout=0.3)  # must not raise


# ── Integration: flag flows from parse → CameraConfig ────────────────────────

class TestDetectionFlagEndToEnd:
    """
    Simulate the full path from raw API JSON to CameraConfig.
    Mimics what the reconcile loop will see after the backend filters
    disabled cameras out: the Vision Service should only receive configs
    with is_detection_enabled = True, but we also verify False is handled
    in case the client-side parse is called with mixed data.
    """

    def _parse_raw(self, raw_items):
        from rtsp.violation_api_client import ViolationApiClient
        client = ViolationApiClient.__new__(ViolationApiClient)
        client._logger = MagicMock()
        return client._parse_cameras(raw_items)

    def test_all_enabled_cameras_pass_through(self):
        items = [
            {
                "id": f"id-{i}", "cameraId": f"CAM-{i}", "tenantId": "t",
                "tenantName": "T", "rtspUrl": f"rtsp://cam/{i}", "whipUrl": "",
                "isStreaming": False, "isDetectionEnabled": True,
                "targetFps": 1.0, "name": f"cam{i}", "location": "L",
                "violationRules": [],
            }
            for i in range(5)
        ]
        configs = self._parse_raw(items)
        assert len(configs) == 5
        assert all(c.is_detection_enabled for c in configs)

    def test_all_disabled_cameras_still_parse_without_error(self):
        """
        Even though the backend normally filters them out, the parse layer
        must handle False cleanly in case of a race condition or direct test.
        """
        item = {
            "id": "id-x", "cameraId": "CAM-X", "tenantId": "t",
            "tenantName": "T", "rtspUrl": "rtsp://cam/x", "whipUrl": "",
            "isStreaming": False, "isDetectionEnabled": False,
            "targetFps": 1.0, "name": "sleeping", "location": "L",
            "violationRules": [],
        }
        configs = self._parse_raw([item])
        assert len(configs) == 1
        assert configs[0].is_detection_enabled is False
