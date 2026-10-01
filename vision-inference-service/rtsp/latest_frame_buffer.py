"""
rtsp/latest_frame_buffer.py
===========================
Thread-safe single-frame ring buffer to decouple RTSP capture from AI inference.

When multiple cameras experience sudden concurrent motion or complex scenes,
synchronous inference in the capture thread can lag behind the RTSP network socket,
causing packet drops or triggering the 5s lag kill-valve.

LatestFrameBuffer allows RTSP capture threads to continuously drain frames at line-rate,
storing only the latest decoded frame. Slow inference passes never block capture, and
inference always processes the freshest available frame.
"""

from __future__ import annotations

import logging
import threading
import time
from typing import Dict, Optional, Tuple
import numpy as np

logger = logging.getLogger("vision-service.rtsp.latest_frame_buffer")


class LatestFrameBuffer:
    """
    Per-camera latest frame ring buffer.
    Overwrites old unconsumed frames to keep latency constant and prevent RAM spikes.
    """

    def __init__(self, max_stale_seconds: float = 2.0):
        self._max_stale_seconds = float(max_stale_seconds)
        # camera_id -> (frame_np, captured_monotonic)
        self._slots: Dict[str, Tuple[np.ndarray, float]] = {}
        self._lock = threading.Lock()

    def put(self, camera_id: str, frame: np.ndarray, timestamp: Optional[float] = None) -> None:
        """
        Store latest frame for camera_id, overwriting previous frame.
        Non-blocking, minimal overhead (<0.05ms).
        """
        if not camera_id or frame is None:
            return
        ts = time.monotonic() if timestamp is None else float(timestamp)
        with self._lock:
            self._slots[camera_id] = (frame, ts)

    def get(self, camera_id: str) -> Tuple[Optional[np.ndarray], float]:
        """
        Get the latest frame without consuming it.
        Returns (frame, timestamp) or (None, 0.0) if missing or stale.
        """
        if not camera_id:
            return (None, 0.0)
        with self._lock:
            entry = self._slots.get(camera_id)
            if not entry:
                return (None, 0.0)
            frame, ts = entry
            if time.monotonic() - ts > self._max_stale_seconds:
                return (None, 0.0)
            return (frame, ts)

    def pop(self, camera_id: str) -> Tuple[Optional[np.ndarray], float]:
        """
        Take and clear the latest frame for camera_id.
        """
        if not camera_id:
            return (None, 0.0)
        with self._lock:
            entry = self._slots.pop(camera_id, None)
            if not entry:
                return (None, 0.0)
            frame, ts = entry
            if time.monotonic() - ts > self._max_stale_seconds:
                return (None, 0.0)
            return (frame, ts)

    def clear_camera(self, camera_id: str) -> None:
        """Clear buffer slot on stream reconnect."""
        if not camera_id:
            return
        with self._lock:
            self._slots.pop(camera_id, None)

    def clear_all(self) -> None:
        with self._lock:
            self._slots.clear()


latest_frame_buffer = LatestFrameBuffer()
