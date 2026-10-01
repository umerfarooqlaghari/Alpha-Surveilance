"""
cache/active_violation_cache.py
===============================
Thread-safe Active Violation Cache & Heartbeat Debouncer for edge surveillance.

Eliminates the continuous HTTP GET /active and PATCH /active flood across the
internet when a subject remains in an ongoing violation state (e.g. unauthorized
area dwell). Instead of querying the cloud on every single frame at 2-5 FPS
(120 GETs + 120 PATCHes per minute), active violation UUIDs are cached locally
and heartbeats are debounced to once every 10 seconds (configurable).
"""

from __future__ import annotations

import json
import logging
import threading
import time
from typing import Any, Dict, Optional, Tuple

logger = logging.getLogger("vision-service.cache.active_violation")

DEFAULT_HEARTBEAT_DEBOUNCE_SECONDS = 10.0
MAX_ACTIVE_PER_CAMERA = 500


class ActiveViolationCache:
    """
    Tracks ongoing active violations per (camera_db_id, track_id).
    Manages debounced heartbeat timings to cut outbound cloud API egress by >95%.
    """

    def __init__(
        self,
        heartbeat_interval: float = DEFAULT_HEARTBEAT_DEBOUNCE_SECONDS,
        max_active_per_camera: int = MAX_ACTIVE_PER_CAMERA,
        redis_client: Optional[Any] = None,
    ):
        self._heartbeat_interval = float(heartbeat_interval)
        self._max_active_per_camera = int(max_active_per_camera)
        self._redis = redis_client
        # camera_db_id -> {track_id -> (violation_uuid, last_heartbeat_monotonic)}
        self._cache: Dict[str, Dict[int, Tuple[str, float]]] = {}
        self._lock = threading.RLock()

    def get_violation_id(self, camera_db_id: str, track_id: int) -> Optional[str]:
        """Return the active violation UUID if present in cache."""
        if not camera_db_id or track_id is None:
            return None
        try:
            track_id = int(track_id)
            if track_id < 0:
                return None
        except (TypeError, ValueError):
            return None

        with self._lock:
            cam_store = self._cache.get(camera_db_id)
            if cam_store:
                entry = cam_store.get(track_id)
                if entry:
                    return entry[0]

        if self._redis is not None:
            try:
                rkey = f"active_viol:{camera_db_id}:{track_id}"
                raw = self._redis.get(rkey)
                if raw:
                    data = json.loads(raw if isinstance(raw, str) else raw.decode("utf-8"))
                    vid = data.get("id")
                    if vid:
                        with self._lock:
                            cam_store = self._cache.setdefault(camera_db_id, {})
                            if len(cam_store) >= self._max_active_per_camera and track_id not in cam_store:
                                oldest_tid = min(cam_store.keys(), key=lambda k: cam_store[k][1])
                                cam_store.pop(oldest_tid, None)
                            cam_store[track_id] = (vid, data.get("last_beat", time.monotonic()))
                        return vid
            except Exception as e:
                logger.debug("[%s] Redis active violation read failed: %s", camera_db_id, e)

        return None

    def register_active(
        self,
        camera_db_id: str,
        track_id: int,
        violation_uuid: str,
        now: Optional[float] = None,
    ) -> None:
        """
        Register a newly created or discovered active violation for a track.
        """
        if not camera_db_id or track_id is None or not violation_uuid:
            return
        try:
            track_id = int(track_id)
            if track_id < 0:
                return
        except (TypeError, ValueError):
            return

        ts = time.monotonic() if now is None else float(now)
        with self._lock:
            cam_store = self._cache.setdefault(camera_db_id, {})
            if len(cam_store) >= self._max_active_per_camera and track_id not in cam_store:
                # Evict oldest entry
                oldest_tid = min(cam_store.keys(), key=lambda k: cam_store[k][1])
                cam_store.pop(oldest_tid, None)
            cam_store[track_id] = (str(violation_uuid), ts)

        if self._redis is not None:
            try:
                rkey = f"active_viol:{camera_db_id}:{track_id}"
                payload = json.dumps({"id": str(violation_uuid), "last_beat": ts})
                self._redis.setex(rkey, 3600, payload)
            except Exception as e:
                logger.debug("[%s] Redis active violation write failed: %s", camera_db_id, e)

    def check_heartbeat(
        self,
        camera_db_id: str,
        track_id: int,
        now: Optional[float] = None,
        interval: Optional[float] = None,
    ) -> Tuple[Optional[str], bool]:
        """
        Check whether an active violation exists and whether a heartbeat update
        should be sent to the cloud API.

        Returns (violation_uuid, should_send):
          - (None, True): No cached violation found. Caller should query API or create.
          - (uuid, True): Debounce interval has elapsed. Caller should send PATCH heartbeat.
          - (uuid, False): Debounced. Heartbeat was sent recently (< interval); skip HTTP call!
        """
        if not camera_db_id or track_id is None:
            return (None, True)
        try:
            track_id = int(track_id)
            if track_id < 0:
                return (None, True)
        except (TypeError, ValueError):
            return (None, True)

        ts = time.monotonic() if now is None else float(now)
        debounce_window = float(interval) if interval is not None else self._heartbeat_interval

        with self._lock:
            cam_store = self._cache.get(camera_db_id)
            if cam_store:
                entry = cam_store.get(track_id)
                if entry:
                    vid, last_beat = entry
                    if ts - last_beat >= debounce_window:
                        return (vid, True)
                    return (vid, False)

        # Not in L1, check L2 Redis
        vid = self.get_violation_id(camera_db_id, track_id)
        if vid:
            return (vid, True)

        return (None, True)

    def record_heartbeat(
        self,
        camera_db_id: str,
        track_id: int,
        now: Optional[float] = None,
    ) -> None:
        """Update last heartbeat timestamp to now."""
        if not camera_db_id or track_id is None:
            return
        try:
            track_id = int(track_id)
            if track_id < 0:
                return
        except (TypeError, ValueError):
            return

        ts = time.monotonic() if now is None else float(now)
        vid = None
        with self._lock:
            cam_store = self._cache.get(camera_db_id)
            if cam_store and track_id in cam_store:
                vid, _ = cam_store[track_id]
                cam_store[track_id] = (vid, ts)

        if self._redis is not None:
            try:
                target_vid = vid or self.get_violation_id(camera_db_id, track_id)
                if target_vid:
                    rkey = f"active_viol:{camera_db_id}:{track_id}"
                    payload = json.dumps({"id": target_vid, "last_beat": ts})
                    self._redis.setex(rkey, 3600, payload)
            except Exception as e:
                logger.debug("[%s] Redis heartbeat update failed: %s", camera_db_id, e)

    def remove(self, camera_db_id: str, track_id: int) -> Optional[str]:
        """
        Remove track from active violation cache when violation concludes or resolves.
        """
        if not camera_db_id or track_id is None:
            return None
        try:
            track_id = int(track_id)
            if track_id < 0:
                return None
        except (TypeError, ValueError):
            return None

        removed_id = None
        with self._lock:
            cam_store = self._cache.get(camera_db_id)
            if cam_store:
                entry = cam_store.pop(track_id, None)
                if entry:
                    removed_id = entry[0]

        if self._redis is not None:
            try:
                self._redis.delete(f"active_viol:{camera_db_id}:{track_id}")
            except Exception as e:
                logger.debug("[%s] Redis active violation delete failed: %s", camera_db_id, e)

        return removed_id

    def clear_camera(self, camera_db_id: str) -> int:
        """Clear all active violations for a camera."""
        if not camera_db_id:
            return 0
        removed = 0
        with self._lock:
            cam_store = self._cache.pop(camera_db_id, None)
            if cam_store:
                removed = len(cam_store)

        if self._redis is not None:
            try:
                pattern = f"active_viol:{camera_db_id}:*"
                keys = list(self._redis.scan_iter(match=pattern, count=100)) if hasattr(self._redis, "scan_iter") else self._redis.keys(pattern)
                if keys:
                    self._redis.delete(*keys)
            except Exception as e:
                logger.debug("[%s] Redis clear active camera failed: %s", camera_db_id, e)

        return removed

    def clear_all(self) -> None:
        """Diagnostic/testing helper."""
        with self._lock:
            self._cache.clear()
        if self._redis is not None:
            try:
                keys = list(self._redis.scan_iter(match="active_viol:*", count=100)) if hasattr(self._redis, "scan_iter") else self._redis.keys("active_viol:*")
                if keys:
                    self._redis.delete(*keys)
            except Exception as e:
                logger.debug("Redis clear all active failed: %s", e)


# Default singleton instance
active_violation_cache = ActiveViolationCache()
