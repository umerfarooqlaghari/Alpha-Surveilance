"""
V5 acceptance tests — fetch_active_cameras failure contract.

    * network error / timeout / 5xx / 401 / 403  →  None   (fetch FAILED)
    * HTTP 200 with empty body                   →  []     (genuinely no cameras)
    * HTTP 200 with cameras                      →  [CameraConfig, ...]
"""
from tests._stubs import install_stubs

install_stubs()

import asyncio  # noqa: E402

import httpx  # noqa: E402
from unittest.mock import AsyncMock  # noqa: E402

from rtsp.violation_api_client import ViolationApiClient  # noqa: E402


def _client() -> ViolationApiClient:
    # max_retries=1 so failure paths return immediately (no backoff sleeps)
    return ViolationApiClient(base_url="http://api.test", api_key="k", max_retries=1)


def _response(status: int, json_body=None) -> httpx.Response:
    return httpx.Response(
        status,
        json=json_body,
        request=httpx.Request("GET", "http://api.test/api/cameras/internal/active"),
    )


def _fetch(get_mock):
    async def _run():
        c = _client()
        c._http.get = get_mock
        try:
            return await c.fetch_active_cameras()
        finally:
            await c.aclose()

    return asyncio.run(_run())


def test_500_returns_none():
    assert _fetch(AsyncMock(return_value=_response(500))) is None


def test_503_returns_none():
    assert _fetch(AsyncMock(return_value=_response(503))) is None


def test_401_returns_none():
    assert _fetch(AsyncMock(return_value=_response(401))) is None


def test_403_returns_none():
    assert _fetch(AsyncMock(return_value=_response(403))) is None


def test_timeout_returns_none():
    assert _fetch(AsyncMock(side_effect=httpx.TimeoutException("boom"))) is None


def test_connect_error_returns_none():
    assert _fetch(AsyncMock(side_effect=httpx.ConnectError("refused"))) is None


def test_unexpected_error_returns_none():
    assert _fetch(AsyncMock(side_effect=ValueError("bad json"))) is None


def test_empty_200_returns_empty_list_not_none():
    result = _fetch(AsyncMock(return_value=_response(200, json_body=[])))
    assert result == []
    assert result is not None


def test_200_with_camera_parses():
    body = [
        {
            "id": "11111111-1111-1111-1111-111111111111",
            "cameraId": "CAM-001",
            "tenantId": "22222222-2222-2222-2222-222222222222",
            "rtspUrl": "rtsp://cam.local/stream",
            "violationRules": [],
        }
    ]
    result = _fetch(AsyncMock(return_value=_response(200, json_body=body)))
    assert result is not None
    assert len(result) == 1
    assert result[0].camera_id == "CAM-001"


def test_transient_then_success_retries():
    """First attempt times out, second succeeds — must return the list."""

    async def _run():
        c = ViolationApiClient(base_url="http://api.test", api_key="k", max_retries=2)
        c._http.get = AsyncMock(
            side_effect=[httpx.TimeoutException("t"), _response(200, json_body=[])]
        )
        try:
            # patch the inter-attempt backoff sleep to keep the test fast
            real_sleep = asyncio.sleep

            async def _fast_sleep(_secs):
                await real_sleep(0)

            asyncio.sleep = _fast_sleep
            try:
                return await c.fetch_active_cameras()
            finally:
                asyncio.sleep = real_sleep
        finally:
            await c.aclose()

    assert asyncio.run(_run()) == []
