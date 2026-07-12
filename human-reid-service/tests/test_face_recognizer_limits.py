"""R5 client-side tests: unknown-embedding enrollment rate limit and HTTP
connection pool sizing in vision-inference-service/inference/face_recognizer.py.

Lives in human-reid-service/tests because the two fixes were shipped together;
imports the vision module via sys.path. Skipped if the vision service's
dependencies (numpy/PIL) are unavailable.
"""
import os
import sys
from unittest import mock

import pytest

_VISION_DIR = os.path.abspath(
    os.path.join(os.path.dirname(__file__), "..", "..", "vision-inference-service")
)

# config.py fails fast on missing production secrets unless TESTING_MODE is on.
os.environ.setdefault("TESTING_MODE", "true")

fr = None
_import_error = None
if os.path.isdir(_VISION_DIR):
    sys.path.insert(0, _VISION_DIR)
    try:
        from inference import face_recognizer as fr  # noqa: E402
    except Exception as exc:  # noqa: BLE001
        _import_error = exc
else:
    _import_error = FileNotFoundError(_VISION_DIR)

pytestmark = pytest.mark.skipif(
    fr is None, reason=f"vision-inference-service not importable: {_import_error}"
)


class _FakeResponse:
    status_code = 201
    text = "ok"


def test_unknown_enrollment_rate_limited_per_camera():
    posts = []

    def fake_post(url, **kwargs):  # __module__ != "requests.api" -> used by _post_reid
        posts.append((url, kwargs))
        return _FakeResponse()

    with mock.patch.object(fr.requests, "post", fake_post):
        fr._LAST_UNKNOWN_ENROLL_BY_CAMERA.clear()
        emb = [0.0] * 128

        # First sighting on cam-1 enrolls.
        assert fr._store_unknown_embedding("tenant-1", emb, "cam-1") is not None
        assert len(posts) == 1

        # Immediate second sighting on the same camera is rate-limited: no
        # HTTP call, no new unknown id.
        assert fr._store_unknown_embedding("tenant-1", emb, "cam-1") is None
        assert len(posts) == 1

        # A different camera has its own budget.
        assert fr._store_unknown_embedding("tenant-1", emb, "cam-2") is not None
        assert len(posts) == 2

        # After the interval elapses, cam-1 may enroll again.
        fr._LAST_UNKNOWN_ENROLL_BY_CAMERA["cam-1"] -= (
            fr.UNKNOWN_ENROLL_MIN_INTERVAL_SECONDS + 1
        )
        assert fr._store_unknown_embedding("tenant-1", emb, "cam-1") is not None
        assert len(posts) == 3

    fr._LAST_UNKNOWN_ENROLL_BY_CAMERA.clear()


def test_rate_limit_applies_to_missing_camera_id_bucket():
    with mock.patch.object(fr.requests, "post", lambda url, **kw: _FakeResponse()):
        fr._LAST_UNKNOWN_ENROLL_BY_CAMERA.clear()
        emb = [0.0] * 128
        assert fr._store_unknown_embedding("tenant-1", emb, None) is not None
        assert fr._store_unknown_embedding("tenant-1", emb, None) is None
    fr._LAST_UNKNOWN_ENROLL_BY_CAMERA.clear()


def test_reid_session_pool_sized_for_thread_pool():
    adapter = fr._REID_SESSION.get_adapter("http://reid.internal")
    assert adapter._pool_maxsize == 32
    adapter = fr._REID_SESSION.get_adapter("https://reid.internal")
    assert adapter._pool_maxsize == 32


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-v"]))
