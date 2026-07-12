from tests._stubs import install_stubs

install_stubs()

import asyncio  # noqa: E402
import types  # noqa: E402

from rtsp.models import CameraConfig  # noqa: E402
from rtsp.stream_manager import CameraStreamManager  # noqa: E402


def _camera(rtsp_url: str) -> CameraConfig:
    return CameraConfig(
        camera_db_id="db-1",
        camera_id="CAM-005",
        tenant_id="tenant-1",
        tenant_name="Alpha Devs",
        rtsp_url=rtsp_url,
    )


class _FakeClient:
    def __init__(self, config: CameraConfig):
        self._config = config
        self.updated_with = None

    def get_state(self):
        return {"status": "reconnecting"}

    def update_config(self, new_config: CameraConfig):
        self.updated_with = new_config


def test_reconcile_restarts_stream_when_rtsp_url_changes(monkeypatch):
    manager = CameraStreamManager(frame_callback=lambda frame, config: None, max_workers=1)
    client = _FakeClient(_camera("rtsp://old.example/mystream"))
    manager._clients[client._config.camera_id] = client

    removed = []
    added = []

    async def fake_remove_camera(self, camera_id: str):
        removed.append(camera_id)
        self._clients.pop(camera_id, None)

    async def fake_add_camera(self, config: CameraConfig):
        added.append(config)

    monkeypatch.setattr(manager, "remove_camera", types.MethodType(fake_remove_camera, manager))
    monkeypatch.setattr(manager, "add_camera", types.MethodType(fake_add_camera, manager))

    new_config = _camera("rtsp://host.docker.internal:8554/mystream")
    asyncio.run(manager.reconcile([new_config]))

    assert removed == ["CAM-005"]
    assert added == [new_config]
    assert client.updated_with is None


def test_reconcile_hot_updates_non_capture_config_without_restart(monkeypatch):
    manager = CameraStreamManager(frame_callback=lambda frame, config: None, max_workers=1)
    client = _FakeClient(_camera("rtsp://host.docker.internal:8554/mystream"))
    manager._clients[client._config.camera_id] = client

    removed = []
    added = []

    async def fake_remove_camera(self, camera_id: str):
        removed.append(camera_id)

    async def fake_add_camera(self, config: CameraConfig):
        added.append(config)

    monkeypatch.setattr(manager, "remove_camera", types.MethodType(fake_remove_camera, manager))
    monkeypatch.setattr(manager, "add_camera", types.MethodType(fake_add_camera, manager))

    new_config = _camera("rtsp://host.docker.internal:8554/mystream")
    new_config.target_fps = 2.0
    asyncio.run(manager.reconcile([new_config]))

    assert removed == []
    assert added == []
    assert client.updated_with == new_config