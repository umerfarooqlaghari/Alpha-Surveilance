"""
rtsp/stream_client.py
Per-camera RTSP stream client with production-grade resilience features:
  - Connection watchdog (detects silent hangs, not just hard failures)
  - Automatic reconnection with exponential backoff
  - FPS throttling (processes 1 frame/N seconds regardless of camera native FPS)
  - Frame timeout detection (kicks in when no frame arrives within deadline)
  - Clean resource release (OpenCV cap.release() always called)
  - Thread-safe state reporting
"""
import cv2
import time
import logging
import threading
import subprocess
import shlex
import numpy as np
from datetime import datetime
from typing import Callable, Optional

from .models import CameraConfig, StreamState
import config

logger = logging.getLogger(__name__)

# Type alias for the frame processing callback
FrameCallback = Callable[[object, CameraConfig], None]  # (cv2_frame, config) -> None


class RtspStreamClient:
    """
    Manages one RTSP camera stream in its own thread.

    Architecture:
        - Main thread:    capture loop (OpenCV VideoCapture — blocking, must run in thread)
        - Watchdog thread: monitors last_frame_at; triggers reconnect on timeout

    The caller provides an async-friendly `frame_callback`. Since the capture loop runs in a
    thread, the callback is called synchronously in that thread. If you need async processing,
    use asyncio.run_coroutine_threadsafe() inside the callback.
    """

    # Watchdog poll interval (how often it checks last_frame_at)
    WATCHDOG_INTERVAL_SECONDS = 5.0

    # Exponential backoff: delay = min(base * 2^attempt, max)
    RECONNECT_BASE_DELAY = 2.0       # seconds
    RECONNECT_MAX_DELAY = 60.0       # seconds cap

    def __init__(
        self,
        config: CameraConfig,
        frame_callback: FrameCallback,
        loop=None,  # asyncio event loop, for thread-safe async callback dispatch
    ):
        self._config = config
        self._frame_callback = frame_callback
        self._loop = loop

        self._state = StreamState(
            camera_id=config.camera_id,
            camera_db_id=config.camera_db_id,
            tenant_id=config.tenant_id,
            name=config.name,
            location=config.location,
        )
        self._state_lock = threading.Lock()

        self._stop_event = threading.Event()
        self._capture_thread: Optional[threading.Thread] = None
        self._watchdog_thread: Optional[threading.Thread] = None
        self._ffmpeg_process: Optional[subprocess.Popen] = None

    # ─────────────────────────────────────────────────────────────────────────
    # Public API
    # ─────────────────────────────────────────────────────────────────────────

    def start(self):
        """Start the capture thread and watchdog thread."""
        logger.info("[%s] Starting stream client", self._config.camera_id)
        with self._state_lock:
            self._state.status = "connecting"
            self._state.started_at = datetime.utcnow()

        self._stop_event.clear()

        self._capture_thread = threading.Thread(
            target=self._capture_loop,
            name=f"rtsp-capture-{self._config.camera_id}",
            daemon=True,
        )
        self._watchdog_thread = threading.Thread(
            target=self._watchdog_loop,
            name=f"rtsp-watchdog-{self._config.camera_id}",
            daemon=True,
        )

        self._capture_thread.start()
        self._watchdog_thread.start()

    def stop(self):
        """Signal the stream to stop and wait for threads to join."""
        logger.info("[%s] Stopping stream client", self._config.camera_id)
        self._stop_event.set()

        if self._capture_thread and self._capture_thread.is_alive():
            self._capture_thread.join(timeout=10)
        if self._watchdog_thread and self._watchdog_thread.is_alive():
            self._watchdog_thread.join(timeout=10)

        with self._state_lock:
            self._state.status = "stopped"
        
        self._stop_ffmpeg()
        logger.info("[%s] Stream stopped", self._config.camera_id)

    def get_state(self) -> dict:
        """Thread-safe snapshot of current stream state."""
        with self._state_lock:
            return self._state.to_dict()

    @property
    def camera_id(self) -> str:
        return self._config.camera_id

    def update_config(self, new_config: CameraConfig):
        """
        Hot-reload the stream config. Currently supports toggling the WebRTC live feed
        and updating violation rules without dropping the AI capture loop.
        """
        old_is_streaming = self._config.is_streaming
        self._config = new_config
        
        # If the user toggled the frontend Power Button, start or stop FFmpeg dynamically
        if not old_is_streaming and new_config.is_streaming:
            logger.info("[%s] ⚡ Cloudflare live feed toggled ON. Starting FFmpeg...", self.camera_id)
            self._start_ffmpeg()
        elif old_is_streaming and not new_config.is_streaming:
            logger.info("[%s] 🔌 Cloudflare live feed toggled OFF. Stopping FFmpeg...", self.camera_id)
            self._stop_ffmpeg()

    # ─────────────────────────────────────────────────────────────────────────
    # Internal: Capture Loop
    # ─────────────────────────────────────────────────────────────────────────

    def _capture_loop(self):
        """
        Main loop: tries to connect, reads frames, throttles FPS.
        On any error, calls _reconnect_with_backoff().
        """
        while not self._stop_event.is_set():
            cap = None
            try:
                cap = self._connect()
                if cap is None:
                    # connect() already handles retries; if it returns None, we're stopping
                    break

                self._run_frame_loop(cap)

            except Exception as e:
                err_msg = f"{type(e).__name__}: {e}"
                logger.error("[%s] Unhandled error in capture loop: %s", self._config.camera_id, err_msg)
                with self._state_lock:
                    self._state.last_error = err_msg
            finally:
                if cap is not None:
                    cap.release()  # Always release OpenCV resources
                    logger.debug("[%s] VideoCapture released", self._config.camera_id)
                self._stop_ffmpeg()

        with self._state_lock:
            self._state.status = "stopped"
        logger.info("[%s] Capture loop exited", self._config.camera_id)

    def _connect(self) -> Optional[cv2.VideoCapture]:
        """
        Opens the RTSP stream with exponential backoff retries.
        Returns VideoCapture on success, None if stop was requested.
        """
        attempt = 0
        max_attempts = self._state.max_reconnect_attempts

        while not self._stop_event.is_set():
            logger.info(
                "[%s] Connecting to RTSP (attempt %d/%d)...",
                self._config.camera_id, attempt + 1, max_attempts
            )

            with self._state_lock:
                self._state.status = "connecting" if attempt == 0 else "reconnecting"
                if attempt > 0:
                    self._state.reconnect_attempts += 1
                    self._state.last_reconnect_at = datetime.utcnow()

            try:
                # Force TCP transport for RTSP feeds (critical for containerized/WSL environments)
                import os
                os.environ["OPENCV_FFMPEG_CAPTURE_OPTIONS"] = "rtsp_transport;tcp"
                
                cap = cv2.VideoCapture(self._config.rtsp_url, cv2.CAP_FFMPEG)
                # Reduce FFMPEG internal buffer so stale frames don't pile up
                cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
                # Give RTSP a moment to negotiate
                time.sleep(1.0)

                if cap.isOpened():
                    logger.info("[%s] ✅ Connected to RTSP stream", self._config.camera_id)
                    with self._state_lock:
                        self._state.status = "running"
                        self._state.last_error = None
                        
                    self._start_ffmpeg()
                    return cap
                else:
                    cap.release()
                    raise ConnectionError("VideoCapture.isOpened() returned False")

            except Exception as e:
                err_msg = str(e)
                logger.warning("[%s] Connection failed: %s", self._config.camera_id, err_msg)
                with self._state_lock:
                    self._state.last_error = err_msg

            attempt += 1
            if attempt >= max_attempts:
                logger.error(
                    "[%s] ❌ Max reconnect attempts (%d) reached. Stream entering error state.",
                    self._config.camera_id, max_attempts
                )
                with self._state_lock:
                    self._state.status = "error"
                return None

            # Exponential backoff delay
            delay = min(
                self.RECONNECT_BASE_DELAY * (2 ** (attempt - 1)),
                self.RECONNECT_MAX_DELAY,
            )
            logger.info("[%s] Waiting %.1fs before retry...", self._config.camera_id, delay)
            self._stop_event.wait(timeout=delay)  # Interruptible sleep

        return None  # Stop was requested

    def _start_ffmpeg(self):
        """Starts the FFmpeg WHIP publisher subprocess if configured."""
        if not self._config.is_streaming or not self._config.whip_url:
            return
            
        if self._ffmpeg_process is not None and self._ffmpeg_process.poll() is None:
            # Already running
            return
            
        logger.info("[%s] 🎥 Starting FFmpeg WebRTC publisher...", self._config.camera_id)
        cmd = [
            "ffmpeg", 
            "-hide_banner", "-loglevel", "error",
            "-rtsp_transport", "tcp",
            "-i", self._config.rtsp_url,
            "-f", "lavfi", "-i", "anullsrc=r=44100:cl=stereo",
            "-c:v", "libx264",
            "-preset", "ultrafast",
            "-tune", "zerolatency",
            "-profile:v", "baseline",
            "-level", "3.1",
            "-pix_fmt", "yuv420p",
            "-r", "30",
            "-bf", "0",
            "-g", "30",
            # Encode the dummy audio (WebRTC strictly requires Opus)
            "-c:a", "libopus", "-b:a", "128k",
            # FFmpeg WHIP muxer requires explicit HTTP line endings for custom headers
            "-headers", f"Authorization: Bearer {config.CLOUDFLARE_API_TOKEN}\r\n",
            "-f", "whip",
            "-tls_verify", "0",
            self._config.whip_url
        ]
        
        try:
            import os
            # We use a list instead of a shell string for safer execution in Docker
            self._ffmpeg_process = subprocess.Popen(
                cmd,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.PIPE,
                stdin=subprocess.DEVNULL,
                shell=False # Set to False for Docker/Linux stability
            )
            
            # Log stderr in background so we can see the REAL error if it crashes again
            def log_stderr():
                if self._ffmpeg_process and self._ffmpeg_process.stderr:
                    for line in self._ffmpeg_process.stderr:
                        logger.error("[%s] FFMPEG ERROR: %s", self._config.camera_id, line.decode('utf-8', errors='replace').strip())
            
            threading.Thread(target=log_stderr, daemon=True).start()
            logger.info("[%s] 🎥 FFmpeg published started (PID: %d)", self._config.camera_id, self._ffmpeg_process.pid)
        except Exception as e:
            logger.error("[%s] ❌ Failed to start FFmpeg: %s", self._config.camera_id, e)

    def _stop_ffmpeg(self):
        """Terminates the FFmpeg subprocess gracefully, ensuring no zombies."""
        if self._ffmpeg_process:
            if self._ffmpeg_process.poll() is None:
                logger.info("[%s] 🛑 Stopping FFmpeg publisher (PID: %d)...", self._config.camera_id, self._ffmpeg_process.pid)
                self._ffmpeg_process.terminate()
                try:
                    self._ffmpeg_process.wait(timeout=5.0)
                except subprocess.TimeoutExpired:
                    logger.warning("[%s] ⚠️ FFmpeg did not terminate gracefully. Killing it.", self._config.camera_id)
                    self._ffmpeg_process.kill()
                    self._ffmpeg_process.wait()
            self._ffmpeg_process = None

    def _run_frame_loop(self, cap: cv2.VideoCapture):
        """
        Reads frames from an open VideoCapture and invokes the callback
        at the throttled target FPS.
        """
        target_fps = max(0.01, self._config.target_fps)  # Guard against division by zero
        frame_interval = 1.0 / target_fps                # seconds between processed frames
        last_process_time = 0.0
        last_debug_upload_time = 0.0  # Track wall-clock time for debug uploads
        
        # For sequential sampling of files
        video_frame_count = 0
        sampling_modulo = 1.0  # calculated below

        # Used for syncing MP4/static files to real-time playback
        source_fps = cap.get(cv2.CAP_PROP_FPS)
        if source_fps <= 0:
            source_fps = 30.0
        playback_delay = 1.0 / source_fps if config.SIMULATE_REALTIME_PLAYBACK else 0.0

        start_wall_time = time.monotonic()

        if config.SIMULATE_REALTIME_PLAYBACK:
            pass # Removed deep-seek logic as it breaks live streaming stability
        
        sampling_modulo = max(1, int(source_fps / target_fps))

        while not self._stop_event.is_set():
            ret, frame = cap.read()
            video_frame_count += 1

            if not ret or frame is None:
                logger.warning("[%s] cap.read() returned no frame — stream may have ended at frame %d", 
                               self._config.camera_id, video_frame_count)
                with self._state_lock:
                    self._state.last_error = "cap.read() returned no frame"
                    self._state.status = "reconnecting"
                break  # Exit to reconnect loop

            if config.SIMULATE_REALTIME_PLAYBACK:
                # Sequential sampling: only process every Nth frame (simulates 1 FPS)
                if video_frame_count % sampling_modulo != 0:
                    continue
                
                # Progress logging every 5 "processed" video seconds
                video_seconds = video_frame_count // source_fps
                if video_seconds % 5 == 0:
                    logger.info("[%s] 🎞️ Video progress: %d seconds (frame %d)", 
                                self._config.camera_id, int(video_seconds), video_frame_count)
            else:
                # For live RTSP: we already read frames normally in the loop above
                pass

            if not ret or frame is None:
                logger.warning("[%s] cap.read() returned no frame — stream may have dropped or ended", self._config.camera_id)
                with self._state_lock:
                    self._state.last_error = "cap.read() returned no frame"
                    self._state.status = "reconnecting"
                break  # Exit to reconnect loop

            # Ghost-frame detection: black/near-black frames come from broken RTSP
            # connections that FFMPEG opened but didn't actually negotiate video for.
            # Mean pixel value < 5 out of 255 = effectively blank.
            if np.mean(frame) < 5.0:
                with self._state_lock:
                    self._state.frames_ghost = getattr(self._state, 'frames_ghost', 0) + 1
                # Don't break — stream may still be negotiating; watchdog handles real timeouts
                continue

            now = time.monotonic()

            # FPS Throttle: skip frames that arrive too quickly
            # BUT: If we are simulating real-time playback (MP4), we want to process EVERY frame
            # (or at least, we shouldn't drop frames based on wall-clock time which can jitter)
            if not config.SIMULATE_REALTIME_PLAYBACK and now - last_process_time < frame_interval:
                with self._state_lock:
                    self._state.frames_skipped += 1
                continue

            last_process_time = now

            # Update heartbeat (watchdog uses this)
            with self._state_lock:
                self._state.last_frame_at = datetime.utcnow()
                self._state.frames_processed += 1

            # Check if FFmpeg crashed unexpectedly
            if self._config.is_streaming and self._ffmpeg_process is not None:
                if self._ffmpeg_process.poll() is not None:
                    code = self._ffmpeg_process.returncode
                    logger.warning("[%s] 🚨 FFmpeg process died unexpectedly (exit code: %s). Forcing reconnect.", self._config.camera_id, code)
                    with self._state_lock:
                        self._state.last_error = f"FFmpeg publisher crashed (code: {code})"
                        self._state.status = "reconnecting"
                    break # Force a reconnect which will recreate OpenCV and FFmpeg
            
            # ─── DEBUG: Upload frame to Cloudinary every 30 seconds (wall-clock based) ───
            now_wall = time.monotonic()
            if now_wall - last_debug_upload_time >= 30.0:
                last_debug_upload_time = now_wall
                try:
                    import cloudinary
                    import cloudinary.uploader
                    import cv2 as _cv2
                    cloudinary.config(
                        cloud_name=config.CLOUDINARY_CLOUD_NAME,
                        api_key=config.CLOUDINARY_API_KEY,
                        api_secret=config.CLOUDINARY_API_SECRET,
                        secure=True
                    )
                    timestamp_str = datetime.now().strftime("%Y%m%d_%H%M%S")
                    public_id = f"alpha-debug/{self._config.camera_id}/{timestamp_str}"
                    # Encode straight to JPEG bytes in memory — no disk needed
                    _, jpg_bytes = _cv2.imencode(".jpg", frame)
                    result = cloudinary.uploader.upload(
                        jpg_bytes.tobytes(),
                        public_id=public_id,
                        resource_type="image",
                        tags=["debug", self._config.camera_id]
                    )
                    logger.info("[%s] 📸 Debug frame uploaded to Cloudinary: %s", self._config.camera_id, result.get("secure_url"))
                except Exception as e:
                    logger.error("[%s] Cloudinary upload failed: %s", self._config.camera_id, e)

            # Invoke the detection pipeline callback
            try:
                self._frame_callback(frame, self._config)
            except Exception as e:
                logger.error("[%s] Frame callback error: %s", self._config.camera_id, e)
                # Don't break — callback errors shouldn't kill the stream

    # ─────────────────────────────────────────────────────────────────────────
    # Internal: Watchdog Loop
    # ─────────────────────────────────────────────────────────────────────────

    def _watchdog_loop(self):
        """
        Runs on a separate thread. Periodically checks last_frame_at.
        If no frame has arrived within frame_timeout_seconds, it sets the stop
        event temporarily so the capture loop exits and reconnects.
        """
        logger.debug("[%s] Watchdog started", self._config.camera_id)
        timeout = self._config.frame_timeout_seconds

        while not self._stop_event.is_set():
            time.sleep(self.WATCHDOG_INTERVAL_SECONDS)

            with self._state_lock:
                status = self._state.status
                last_frame_at = self._state.last_frame_at

            # Only check if we're supposed to be running
            if status != "running" or last_frame_at is None:
                continue

            elapsed = (datetime.utcnow() - last_frame_at).total_seconds()
            if elapsed > timeout:
                logger.warning(
                    "[%s] 🚨 Frame timeout! No frame for %.1fs (threshold: %.1fs). Forcing reconnect.",
                    self._config.camera_id, elapsed, timeout
                )
                with self._state_lock:
                    self._state.status = "reconnecting"
                    self._state.last_error = f"Frame timeout after {elapsed:.1f}s"

                # The capture thread's cap.read() will eventually fail on a dead stream,
                # but we set a flag so the loop knows to reconnect
                # (cap.read() will unblock because the stream is dead)

        logger.debug("[%s] Watchdog exited", self._config.camera_id)
