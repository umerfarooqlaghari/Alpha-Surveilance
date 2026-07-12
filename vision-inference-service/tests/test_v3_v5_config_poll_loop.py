"""
V3(b) + V5 acceptance tests — _config_poll_loop reconcile behaviour.

  * fetch returns None (API failure)  → reconcile NOT called, loop keeps going
  * fetch returns a list (success)    → reconcile IS called — every poll,
                                        even when the config signature did not
                                        change (so stopped/error streams get
                                        restarted by reconcile's dead-stream
                                        sweep).
"""
from tests._stubs import install_stubs

install_stubs()

import asyncio  # noqa: E402

import pytest  # noqa: E402
from unittest.mock import AsyncMock, MagicMock  # noqa: E402

import main  # noqa: E402


def _drive_poll_loop(monkeypatch, fetch_side_effects, last_signature=None):
    """Run _config_poll_loop until fetch_active_cameras raises CancelledError.

    Returns the stream_manager mock for assertions.
    """
    api = MagicMock()
    api.fetch_active_cameras = AsyncMock(side_effect=fetch_side_effects)
    sm = MagicMock()
    sm.reconcile = AsyncMock()

    monkeypatch.setattr(main, "api_client", api)
    monkeypatch.setattr(main, "stream_manager", sm)
    monkeypatch.setattr(main, "edge_device_id", None)
    monkeypatch.setattr(main, "_last_config_signature", last_signature)
    # interval=0 → asyncio.sleep(0) just yields; the loop is terminated by the
    # CancelledError placed at the end of fetch_side_effects.
    monkeypatch.setattr(main.config, "CONFIG_POLL_INTERVAL_SECONDS", 0)

    async def _run():
        with pytest.raises(asyncio.CancelledError):
            await main._config_poll_loop()

    asyncio.run(_run())
    return sm, api


def test_poll_does_not_reconcile_on_fetch_failure(monkeypatch):
    sm, api = _drive_poll_loop(
        monkeypatch, [None, asyncio.CancelledError()]
    )
    sm.reconcile.assert_not_called()
    assert api.fetch_active_cameras.await_count == 2  # loop survived the failure


def test_poll_survives_repeated_failures_without_teardown(monkeypatch):
    sm, api = _drive_poll_loop(
        monkeypatch, [None, None, None, asyncio.CancelledError()]
    )
    sm.reconcile.assert_not_called()
    assert api.fetch_active_cameras.await_count == 4


def test_poll_reconciles_on_success(monkeypatch):
    sm, _ = _drive_poll_loop(monkeypatch, [[], asyncio.CancelledError()])
    sm.reconcile.assert_awaited_once_with([])


def test_poll_reconciles_even_when_signature_unchanged(monkeypatch):
    """V3 fix: unconditional reconcile on success restarts dead streams."""
    sm, _ = _drive_poll_loop(
        monkeypatch,
        [[], [], asyncio.CancelledError()],
        last_signature=main._camera_config_signature([]),
    )
    assert sm.reconcile.await_count == 2


def test_poll_failure_then_success_reconciles_once(monkeypatch):
    sm, _ = _drive_poll_loop(
        monkeypatch, [None, [], asyncio.CancelledError()]
    )
    sm.reconcile.assert_awaited_once_with([])
