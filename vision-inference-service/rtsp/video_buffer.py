"""
rtsp/video_buffer.py
Thread-safe circular frame buffer that stores recent camera frames for
event-driven video clip extraction (e.g. 2-3 second violation clips).
"""
import time
import cv2
import threading
import numpy as np
from collections import deque
from typing import List, Tuple, Optional


class RollingFrameBuffer:
    """
    Maintains a rolling in-memory buffer of recent frames for a camera stream.
    Sized for short pre/post-event windows (e.g. 3-4 seconds total).

    Timestamps are ``time.monotonic()`` seconds. Callers MUST use the same clock
    for :meth:`push` and :meth:`get_window` — mixing in ``time.time()`` silently
    yields an empty window.
    """

    def __init__(
        self,
        max_duration: float = 4.5,
        target_fps: float = 15.0,
        max_dimension: int = 720,
    ):
        """
        :param max_duration: Maximum duration of frames to keep in seconds (default: 4.5s).
        :param target_fps: Target frame rate stored into buffer (default: 15.0 FPS).
        :param max_dimension: Max height or width to downscale frames to conserve RAM (default: 720p).
        """
        self.max_duration = float(max_duration)
        self.target_fps = max(1.0, float(target_fps))
        self.max_dimension = int(max_dimension)
        self.max_frames = max(15, int(self.max_duration * self.target_fps))

        # Stores (wall_clock_timestamp: float, frame_bgr: np.ndarray)
        self._buffer: deque[Tuple[float, np.ndarray]] = deque(maxlen=self.max_frames)
        self._lock = threading.Lock()
        self._min_interval = 1.0 / self.target_fps
        self._last_push_time = 0.0

    def push(self, frame: np.ndarray, timestamp: Optional[float] = None) -> bool:
        """
        Pushes a new frame into the circular buffer at the configured target FPS rate.
        Returns True if the frame was accepted, False if throttled.
        """
        if frame is None or frame.size == 0:
            return False

        now = timestamp if timestamp is not None else time.monotonic()
        with self._lock:
            # Throttle ingestion to target_fps
            if now - self._last_push_time < (self._min_interval * 0.9):
                return False
            self._last_push_time = now

            # Downscale if larger than max_dimension to preserve memory
            h, w = frame.shape[:2]
            if max(h, w) > self.max_dimension and self.max_dimension > 0:
                if h >= w:
                    new_h = self.max_dimension
                    new_w = int(w * (new_h / h))
                else:
                    new_w = self.max_dimension
                    new_h = int(h * (new_w / w))
                # Ensure dimensions are even for H.264 encoding compatibility
                new_w = new_w if new_w % 2 == 0 else new_w - 1
                new_h = new_h if new_h % 2 == 0 else new_h - 1
                stored_frame = cv2.resize(frame, (new_w, new_h), interpolation=cv2.INTER_AREA)
            else:
                # Make sure even dimensions
                if (w % 2 != 0) or (h % 2 != 0):
                    even_w = w if w % 2 == 0 else w - 1
                    even_h = h if h % 2 == 0 else h - 1
                    stored_frame = cv2.resize(frame, (even_w, even_h), interpolation=cv2.INTER_AREA)
                else:
                    stored_frame = frame.copy()

            self._buffer.append((now, stored_frame))
            return True

    def get_window(self, start_ts: float, end_ts: float) -> List[Tuple[float, np.ndarray]]:
        """
        Extracts buffered frames whose timestamps fall within [start_ts, end_ts].

        Returns ONLY frames genuinely inside the window — an empty list when the
        window has already aged out of the buffer.

        Audit P1: this used to fall back to returning the *entire* buffer when the
        window matched nothing, which silently attached footage from a completely
        different moment to a violation (verified: a request for t+0.5..t+3.5
        returned frames spanning t+8.9..t+13.3). For an evidence product, no clip
        is strictly better than the wrong clip, so the caller now decides what to
        do with an empty or short result.
        """
        with self._lock:
            if not self._buffer:
                return []
            return [
                (ts, frame.copy())
                for ts, frame in self._buffer
                if start_ts <= ts <= end_ts
            ]

    def coverage(self, start_ts: float, end_ts: float) -> float:
        """
        Fraction (0.0-1.0) of the requested window actually held in the buffer,
        measured against the frame count the window *should* contain at
        ``target_fps``. Lets callers reject a clip that only caught the tail end
        of its own window. Cheap: no frame copies.
        """
        span = max(0.0, float(end_ts) - float(start_ts))
        if span <= 0:
            return 0.0
        expected = max(1.0, span * self.target_fps)
        with self._lock:
            have = sum(1 for ts, _ in self._buffer if start_ts <= ts <= end_ts)
        return min(1.0, have / expected)

    def get_latest_frames(self, count: int) -> List[Tuple[float, np.ndarray]]:
        """Extracts the most recent `count` frames."""
        with self._lock:
            items = list(self._buffer)
            return [(ts, frame.copy()) for ts, frame in items[-count:]]

    def clear(self) -> None:
        """Clears the buffer."""
        with self._lock:
            self._buffer.clear()
            self._last_push_time = 0.0

    def __len__(self) -> int:
        with self._lock:
            return len(self._buffer)
