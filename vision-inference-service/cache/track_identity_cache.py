"""
cache/track_identity_cache.py
=============================
Thread-safe, high-performance Track-to-Identity Cache for edge surveillance.

Prevents repeated facial recognition and Re-ID network passes for the same
tracked subject across video frames. Once a track (e.g. Track #3) is resolved
to an employee or unknown visitor, the identity is cached for a configurable TTL
(default: 30 seconds).
"""

from __future__ import annotations

import json
import logging
import threading
import time
from typing import Any, Dict, Optional, Tuple

logger = logging.getLogger("vision-service.cache.track_identity")

DEFAULT_TRACK_IDENTITY_TTL_SECONDS = 30.0
MAX_TRACKS_PER_CAMERA = 500


class TrackIdentityCache:
    """
    Two-tier (L1 In-Memory + optional L2 Redis) cache mapping:
    (camera_id, track_id) -> identity_dict
    """

    def __init__(
        self,
        default_ttl: float = DEFAULT_TRACK_IDENTITY_TTL_SECONDS,
        max_tracks_per_camera: int = MAX_TRACKS_PER_CAMERA,
        redis_client: Optional[Any] = None,
    ):
        self._default_ttl = float(default_ttl)
        self._max_tracks_per_camera = int(max_tracks_per_camera)
        self._redis = redis_client
        # L1: camera_id -> {track_id -> (identity_dict, expires_at_monotonic)}
        self._cache: Dict[str, Dict[int, Tuple[dict, float]]] = {}
        self._lock = threading.RLock()

    def get(self, camera_id: str, track_id: int) -> Optional[dict]:
        """
        Retrieve cached identity for a track if present and unexpired.
        Returns None on cache miss or expiration.
        """
        if not camera_id or track_id is None:
            return None
        try:
            track_id = int(track_id)
            if track_id < 0:
                return None
        except (TypeError, ValueError):
            return None

        now = time.monotonic()

        # 1. Check L1 in-memory cache
        with self._lock:
            cam_store = self._cache.get(camera_id)
            if cam_store:
                entry = cam_store.get(track_id)
                if entry:
                    ident, expires_at = entry
                    if now < expires_at:
                        return dict(ident)
                    # Expired -> prune
                    cam_store.pop(track_id, None)

        # 2. Check L2 Redis cache if configured
        if self._redis is not None:
            try:
                rkey = f"reid:track:{camera_id}:{track_id}"
                raw = self._redis.get(rkey)
                if raw:
                    ident = json.loads(raw if isinstance(raw, str) else raw.decode("utf-8"))
                    # Re-populate L1
                    with self._lock:
                        cam_store = self._cache.setdefault(camera_id, {})
                        cam_store[track_id] = (ident, now + self._default_ttl)
                    return dict(ident)
            except Exception as e:
                logger.debug("[%s] Redis track identity read failed: %s", camera_id, e)

        return None

    def set(
        self,
        camera_id: str,
        track_id: int,
        identity: dict,
        ttl_seconds: Optional[float] = None,
    ) -> None:
        """
        Store identity for a track with a TTL.
        """
        if not camera_id or track_id is None or identity is None:
            return
        try:
            track_id = int(track_id)
            if track_id < 0:
                return
        except (TypeError, ValueError):
            return

        ttl = float(ttl_seconds) if ttl_seconds is not None and ttl_seconds > 0 else self._default_ttl
        now = time.monotonic()
        expires_at = now + ttl

        # 1. Update L1
        with self._lock:
            cam_store = self._cache.setdefault(camera_id, {})
            # Prune stale tracks if reaching camera capacity limit
            if len(cam_store) >= self._max_tracks_per_camera and track_id not in cam_store:
                stale = [tid for tid, (_, exp) in cam_store.items() if exp <= now]
                for tid in stale:
                    cam_store.pop(tid, None)
                # If still over limit, drop the oldest entry
                if len(cam_store) >= self._max_tracks_per_camera:
                    oldest_tid = min(cam_store.keys(), key=lambda k: cam_store[k][1])
                    cam_store.pop(oldest_tid, None)

            cam_store[track_id] = (dict(identity), expires_at)

        # 2. Update L2 Redis if available
        if self._redis is not None:
            try:
                rkey = f"reid:track:{camera_id}:{track_id}"
                payload = json.dumps(identity)
                self._redis.setex(rkey, max(1, int(ttl)), payload)
            except Exception as e:
                logger.debug("[%s] Redis track identity write failed: %s", camera_id, e)

    def invalidate_track(self, camera_id: str, track_id: int) -> None:
        """Evict a single track identity."""
        if not camera_id or track_id is None:
            return
        try:
            track_id = int(track_id)
            if track_id < 0:
                return
        except (TypeError, ValueError):
            return

        with self._lock:
            cam_store = self._cache.get(camera_id)
            if cam_store:
                cam_store.pop(track_id, None)

        if self._redis is not None:
            try:
                self._redis.delete(f"reid:track:{camera_id}:{track_id}")
            except Exception as e:
                logger.debug("[%s] Redis track invalidate failed: %s", camera_id, e)

    def clear_camera(self, camera_id: str) -> int:
        """
        Evict all track identities for a camera (e.g. on stream reconnect or teardown).
        """
        if not camera_id:
            return 0
        removed = 0
        with self._lock:
            cam_store = self._cache.pop(camera_id, None)
            if cam_store:
                removed = len(cam_store)

        if self._redis is not None:
            try:
                pattern = f"reid:track:{camera_id}:*"
                keys = list(self._redis.scan_iter(match=pattern, count=100)) if hasattr(self._redis, "scan_iter") else self._redis.keys(pattern)
                if keys:
                    self._redis.delete(*keys)
            except Exception as e:
                logger.debug("[%s] Redis clear camera failed: %s", camera_id, e)

        return removed

    def clear_all(self) -> None:
        """Diagnostic/testing helper to clear all entries."""
        with self._lock:
            self._cache.clear()
        if self._redis is not None:
            try:
                keys = list(self._redis.scan_iter(match="reid:track:*", count=100)) if hasattr(self._redis, "scan_iter") else self._redis.keys("reid:track:*")
                if keys:
                    self._redis.delete(*keys)
            except Exception as e:
                logger.debug("Redis clear all failed: %s", e)


# Default singleton instance
track_identity_cache = TrackIdentityCache()
