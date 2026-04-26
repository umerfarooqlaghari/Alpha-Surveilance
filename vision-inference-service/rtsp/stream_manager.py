"""
rtsp/stream_manager.py
Orchestrates thousands of per-camera RtspStreamClient instances.

Design:
  - Each camera runs in its own daemon thread (via RtspStreamClient)
  - The manager itself is fully async-compatible (lives in the FastAPI event loop)
  - Supports hot add/remove/reconcile without service restart
  - Thread-safe registry of all stream states
"""
import asyncio
import logging
from concurrent.futures import ThreadPoolExecutor
from typing import Dict, List, Optional

from .models import CameraConfig, StreamState
from .stream_client import RtspStreamClient, FrameCallback

logger = logging.getLogger(__name__)


class CameraStreamManager:
    """
    Top-level manager for all camera streams.

    Usage:
        manager = CameraStreamManager(frame_callback=my_callback, max_workers=500)
        await manager.start_all(camera_configs)
        ...
        await manager.stop_all()
    """

    def __init__(
        self,
        frame_callback: FrameCallback,
        max_workers: int = 500,
    ):
        self._frame_callback = frame_callback
        self._max_workers = max_workers

        # camera_id -> RtspStreamClient
        self._clients: Dict[str, RtspStreamClient] = {}
        self._lock = asyncio.Lock()  # protects _clients dict

        # Thread pool for blocking OpenCV operations
        self._executor = ThreadPoolExecutor(max_workers=max_workers, thread_name_prefix="rtsp-worker")

        self._running = False

    # ─────────────────────────────────────────────────────────────────────────
    # Lifecycle
    # ─────────────────────────────────────────────────────────────────────────

    async def start_all(self, cameras: List[CameraConfig]):
        """
        Start streams for all given cameras. Called at service startup.
        Cameras already running are skipped (idempotent).
        """
        if not cameras:
            logger.warning("start_all() called with empty camera list — no streams to start")
            return

        self._running = True
        loop = asyncio.get_event_loop()

        async with self._lock:
            for config in cameras:
                if config.camera_id in self._clients:
                    logger.debug("[%s] Stream already running, skipping", config.camera_id)
                    continue
                await self._start_one(config, loop)

        logger.info("🚀 Started %d camera streams", len(self._clients))

    async def stop_all(self):
        """Gracefully stop all running streams. Called at service shutdown."""
        logger.info("Stopping all %d streams...", len(self._clients))
        self._running = False

        async with self._lock:
            clients_snapshot = list(self._clients.values())

        # Stop all clients concurrently via thread pool
        loop = asyncio.get_event_loop()
        stop_tasks = [
            loop.run_in_executor(self._executor, client.stop)
            for client in clients_snapshot
        ]
        await asyncio.gather(*stop_tasks, return_exceptions=True)

        async with self._lock:
            self._clients.clear()

        self._executor.shutdown(wait=False)
        logger.info("✅ All streams stopped")

    async def add_camera(self, config: CameraConfig):
        """Dynamically add a new camera stream without restarting the service."""
        loop = asyncio.get_event_loop()
        async with self._lock:
            if config.camera_id in self._clients:
                logger.info("[%s] add_camera: already exists, ignoring", config.camera_id)
                return
            await self._start_one(config, loop)

    async def remove_camera(self, camera_id: str):
        """Dynamically remove and stop a camera stream."""
        async with self._lock:
            client = self._clients.pop(camera_id, None)

        if client:
            loop = asyncio.get_event_loop()
            await loop.run_in_executor(self._executor, client.stop)
            logger.info("[%s] Camera stream removed", camera_id)
        else:
            logger.warning("[%s] remove_camera: not found", camera_id)

    async def reconcile(self, new_configs: List[CameraConfig]):
        """
        Hot-reload: syncs running streams with the latest camera list from the API.
          - Starts streams for new cameras
          - Stops streams for removed cameras
          - Restarts streams that are in 'stopped' or 'error' state (e.g. exhausted retries)
          - Ignores cameras that are still actively running (no restart)
        """
        new_ids = {c.camera_id for c in new_configs}

        async with self._lock:
            current_ids = set(self._clients.keys())

        to_add = [c for c in new_configs if c.camera_id not in current_ids]
        to_remove = current_ids - new_ids

        # Also restart any streams that have died (stopped/error) so they recover on reload
        dead_ids = set()
        to_update = []
        
        for camera_id, client in list(self._clients.items()):
            state = client.get_state()
            if state.get("status") in ("stopped", "error") and camera_id in new_ids:
                dead_ids.add(camera_id)
            elif camera_id in new_ids:
                # Still running, check if its configuration drifted (e.g. user toggled web stream on/off)
                new_cfg = next(c for c in new_configs if c.camera_id == camera_id)
                if client._config.is_streaming != new_cfg.is_streaming or client._config.violation_rules != new_cfg.violation_rules:
                    to_update.append((client, new_cfg))

        logger.info(
            "Reconciling streams: +%d new, -%d removed, %d updated, %d unchanged, %d dead→restart",
            len(to_add), len(to_remove), len(to_update), len(current_ids - new_ids - dead_ids) - len(to_update), len(dead_ids)
        )

        # Stop dead streams first so they can be re-added below
        for camera_id in dead_ids:
            logger.info("[%s] Restarting dead stream (status was stopped/error)", camera_id)
            await self.remove_camera(camera_id)

        for camera_id in to_remove:
            await self.remove_camera(camera_id)

        # Add new + previously dead cameras
        cameras_to_start = to_add + [c for c in new_configs if c.camera_id in dead_ids]
        for config in cameras_to_start:
            await self.add_camera(config)
            
        # Hot-reload configurations for running streams (e.g., toggle FFmpeg Cloudflare feed without tearing down AI)
        for client, new_cfg in to_update:
            client.update_config(new_cfg)


    # ─────────────────────────────────────────────────────────────────────────
    # State inspection
    # ─────────────────────────────────────────────────────────────────────────

    def get_all_states(self) -> List[dict]:
        """Returns a snapshot of all stream states. Safe to call from async context."""
        return [client.get_state() for client in self._clients.values()]

    def get_camera_state(self, camera_id: str) -> Optional[dict]:
        client = self._clients.get(camera_id)
        return client.get_state() if client else None

    @property
    def active_count(self) -> int:
        return len(self._clients)

    # ─────────────────────────────────────────────────────────────────────────
    # Internal
    # ─────────────────────────────────────────────────────────────────────────

    async def _start_one(self, config: CameraConfig, loop):
        """
        Creates and starts a single RtspStreamClient.
        Must be called while holding self._lock.
        """
        client = RtspStreamClient(
            config=config,
            frame_callback=self._frame_callback,
            loop=loop,
        )
        self._clients[config.camera_id] = client

        # Start the blocking client in the thread pool
        loop.run_in_executor(self._executor, client.start)
        logger.info("[%s] Stream client launched (tenant=%s, url=%s...)",
                    config.camera_id, config.tenant_id, config.rtsp_url[:30])
