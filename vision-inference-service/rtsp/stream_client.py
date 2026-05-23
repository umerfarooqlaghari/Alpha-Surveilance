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
import socket
import time
import logging
import threading
import subprocess
import shlex
import numpy as np
from datetime import datetime, timezone, time as dt_time
from typing import Callable, Optional
from urllib.parse import urlparse

from .models import CameraConfig, StreamState, DetectionScheduleItem
import config

logger = logging.getLogger(__name__)

# Type alias for the frame processing callback
FrameCallback = Callable[[object, CameraConfig], None]  # (cv2_frame, config) -> None


def _send_rtsp_teardown(rtsp_url: str, timeout: float = 2.0) -> None:
    """
    Best-effort RTSP TEARDOWN before releasing an OpenCV VideoCapture.

    OpenCV's ``cap.release()`` closes the underlying TCP socket without first
    sending an RTSP TEARDOWN.  Single-client RTSP servers (OctoRTSP, many
    IP cameras) then consider the session still active and refuse the next
    connection attempt — the exact symptom that caused CAM-004 to stop working
    after the first successful violation.

    Since we don't have the RTSP Session ID that OpenCV negotiated internally,
    we open a *fresh* TCP connection and send a bare TEARDOWN for the stream
    URI.  RFC 2326 §10.4 says a server MUST accept TEARDOWN without a Session
    header (it tears down all sessions for that URI).  In practice this causes
    OctoRTSP, MediaMTX, and most Hikvision / Dahua firmware to immediately free
    the slot.

    Any exception is swallowed — TEARDOWN is best-effort; the calling code will
    still proceed with ``cap.release()`` regardless.
    """
    try:
        parsed = urlparse(rtsp_url)
        host = parsed.hostname
        port = parsed.port or 554
        # Build a clean URI without credentials (RFC 2326 forbids userinfo in request lines)
        path = parsed.path or "/"
        clean_uri = f"rtsp://{host}:{port}{path}"
        teardown = (
            f"TEARDOWN {clean_uri} RTSP/1.0\r\n"
            f"CSeq: 1\r\n"
            f"User-Agent: alpha-vision-inference/1.0\r\n"
            f"\r\n"
        )
        with socket.create_connection((host, port), timeout=timeout) as sock:
            sock.sendall(teardown.encode("ascii"))
            # Read the response non-blockingly so the OS flushes the send buffer
            sock.settimeout(0.3)
            try:
                sock.recv(256)
            except (socket.timeout, OSError):
                pass
        logger.debug("TEARDOWN sent to %s:%d", host, port)
    except Exception:  # noqa: BLE001
        # Non-fatal: network may already be gone, or server doesn't support TEARDOWN
        logger.debug("TEARDOWN skipped for %s (server unreachable or refused)", rtsp_url)


def _is_in_sleep_window(schedule: DetectionScheduleItem, utc_now: datetime) -> bool:
    """
    Returns True if *utc_now* falls inside the sleep window described by *schedule*.

    DaysOfWeek bitmask uses .NET DayOfWeek values so they stay consistent with
    what the backend stores and returns:
      Sunday=1, Monday=2, Tuesday=4, Wednesday=8, Thursday=16, Friday=32, Saturday=64.
    0 or 127 means "every day".

    Overnight windows (StartTime > EndTime, e.g. 22:00 → 06:00) are supported.
    """
    if schedule.days_of_week not in (0, 127):
        # Python weekday(): Monday=0 … Sunday=6
        # .NET DayOfWeek:   Sunday=0, Monday=1 … Saturday=6
        cs_day = (utc_now.weekday() + 1) % 7  # Mon→1, Tue→2, …, Sun→0
        day_bit = 1 << cs_day
        if not (schedule.days_of_week & day_bit):
            return False

    try:
        sh, sm = map(int, schedule.start_time.split(":"))
        eh, em = map(int, schedule.end_time.split(":"))
    except (ValueError, AttributeError):
        return False

    start = dt_time(sh, sm)
    end   = dt_time(eh, em)
    current = utc_now.time().replace(second=0, microsecond=0)

    if start <= end:
        return start <= current < end
    else:            # overnight window
        return current >= start or current < end


def _camera_is_asleep(camera_config: CameraConfig, utc_now: datetime) -> bool:
    """Return True if the camera is currently inside any active detection sleep window."""
    return any(
        _is_in_sleep_window(s, utc_now)
        for s in camera_config.detection_schedules
        if s.is_active
    )


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
        on_reconnect: Optional[Callable[[str], None]] = None,  # C-3: invoked with camera_id on every reconnect
    ):
        self._config = config
        self._frame_callback = frame_callback
        self._loop = loop
        self._on_reconnect = on_reconnect

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
            self._state.started_at = datetime.now(timezone.utc)

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
                    # Send RTSP TEARDOWN before releasing so single-client servers
                    # (OctoRTSP, etc.) free their slot immediately rather than
                    # waiting for a TCP timeout.  Must happen BEFORE cap.release()
                    # which closes the socket without a protocol-level goodbye.
                    _send_rtsp_teardown(self._config.rtsp_url)
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
                    self._state.last_reconnect_at = datetime.now(timezone.utc)

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

                    # C-3 fix: notify owner that this stream just (re)connected so any
                    # tracker / violation state from before the disconnect can be cleared.
                    # Skipped on the very first connect (attempt == 0) since there is
                    # no prior state to reset.
                    if attempt > 0 and self._on_reconnect is not None:
                        try:
                            self._on_reconnect(self._config.camera_id)
                        except Exception:  # noqa: BLE001
                            logger.exception("[%s] on_reconnect callback failed", self._config.camera_id)

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
            # FOR LIVE RTSP: Drain the buffer to eliminate lag
            # We grab all waiting frames but only retrieve/process the last one
            if not config.SIMULATE_REALTIME_PLAYBACK:
                grab_count = 0
                max_lag_frames = int(config.MAX_STREAM_LAG_SECONDS * 30) # Assuming 30fps camera max
                
                while True:
                    grabbed = cap.grab()
                    if not grabbed:
                        break
                    grab_count += 1
                    
                    # If we have dropped a massive amount of frames (e.g. 4 minutes worth),
                    # our drain loop will be too slow. We must KILL the connection.
                    if grab_count > max_lag_frames:
                        logger.warning("[%s] 🚨 Stream lag exceeded threshold (%d frames). Kicking Kill Valve...", 
                                       self._config.camera_id, grab_count)
                        with self._state_lock:
                            self._state.status = "reconnecting"
                            self._state.last_error = f"Stream lag exceeded {config.MAX_STREAM_LAG_SECONDS}s"
                        break # This will break the inner loop, but we need to break the outer too
                    
                    now = time.monotonic()
                    if now - last_process_time >= frame_interval:
                        ret, frame = cap.retrieve()
                        break
                    time.sleep(0.001) 
                
                if self._state.status == "reconnecting":
                    break # Force outer loop to reconnect

                if not grabbed or not ret:
                    time.sleep(0.01)
                    continue
            else:
                # FOR SIMULATION: Standard sequential read
                ret, frame = cap.read()
                video_frame_count += 1
                
                if not ret or frame is None:
                    logger.warning("[%s] cap.read() returned no frame — stream may have ended at frame %d", 
                                   self._config.camera_id, video_frame_count)
                    break
                
                # Sequential sampling: only process every Nth frame (simulates 1 FPS)
                if video_frame_count % sampling_modulo != 0:
                    continue

            # Ghost-frame detection: black/near-black frames come from broken RTSP
            # connections that FFMPEG opened but didn't actually negotiate video for.
            # Mean pixel value < 5 out of 255 = effectively blank.
            if np.mean(frame) < 5.0:
                with self._state_lock:
                    self._state.frames_ghost = getattr(self._state, 'frames_ghost', 0) + 1
                # Don't break — stream may still be negotiating; watchdog handles real timeouts
                continue

            last_process_time = now

            # Update heartbeat (watchdog uses this)
            with self._state_lock:
                self._state.last_frame_at = datetime.now(timezone.utc)
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
            # Runs in a daemon thread so the blocking HTTP upload never stalls the drain loop.
            now_wall = time.monotonic()
            if now_wall - last_debug_upload_time >= 30.0:
                last_debug_upload_time = now_wall
                import cv2 as _cv2
                _, jpg_bytes = _cv2.imencode(".jpg", frame)
                _upload_bytes = jpg_bytes.tobytes()
                _camera_id    = self._config.camera_id

                def _cloudinary_upload(raw: bytes, cam_id: str) -> None:
                    try:
                        import cloudinary
                        import cloudinary.uploader
                        cloudinary.config(
                            cloud_name=config.CLOUDINARY_CLOUD_NAME,
                            api_key=config.CLOUDINARY_API_KEY,
                            api_secret=config.CLOUDINARY_API_SECRET,
                            secure=True
                        )
                        timestamp_str = datetime.now().strftime("%Y%m%d_%H%M%S")
                        public_id = f"alpha-debug/{cam_id}/{timestamp_str}"
                        result = cloudinary.uploader.upload(
                            raw,
                            public_id=public_id,
                            resource_type="image",
                            tags=["debug", cam_id]
                        )
                        logger.info("[%s] 📸 Debug frame uploaded to Cloudinary: %s", cam_id, result.get("secure_url"))
                    except Exception as e:
                        logger.error("[%s] Cloudinary upload failed: %s", cam_id, e)

                import threading
                threading.Thread(
                    target=_cloudinary_upload,
                    args=(_upload_bytes, _camera_id),
                    daemon=True,
                    name=f"cloudinary-{_camera_id}",
                ).start()

            # Invoke the detection pipeline callback
            try:
                # ── Schedule check: skip inference during sleep windows ──────
                # We still read/drain frames to keep the RTSP stream alive and
                # the watchdog heartbeat ticking.  Only the AI callback is skipped.
                if _camera_is_asleep(self._config, datetime.now(timezone.utc)):
                    logger.debug(
                        "[%s] In detection sleep window — skipping inference for this frame",
                        self._config.camera_id,
                    )
                    continue

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

            elapsed = (datetime.now(timezone.utc) - last_frame_at).total_seconds()
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
