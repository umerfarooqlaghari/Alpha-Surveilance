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
import os
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

# H1 fix: OpenCV reads OPENCV_FFMPEG_CAPTURE_OPTIONS once per VideoCapture
# construction, but the previous implementation mutated this process-global
# inside the per-camera _connect() path on every reconnect. With multiple
# capture threads writing the same global concurrently the value was racy,
# and the mutation also bled into unrelated tests that imported the module.
# Set it once here at module load and leave it alone.
#
# V4 fix: `stimeout` is FFmpeg's RTSP TCP socket timeout in MICROSECONDS
# (5_000_000 = 5s). Without it, cap.grab() on a half-dead camera connection
# can block forever — the watchdog flips state to "reconnecting" but the
# capture thread never wakes up to act on it. Tunable via
# RTSP_SOCKET_TIMEOUT_SECONDS.
# NOTE on ffmpeg versions: the option was renamed `stimeout` -> `timeout`
# in ffmpeg 5.0 for the RTSP demuxer; most distro builds still accept
# `stimeout` as a deprecated alias, and unknown AVOptions passed via
# OPENCV_FFMPEG_CAPTURE_OPTIONS are logged and ignored rather than fatal,
# so shipping `stimeout` is the safest cross-version choice.
_RTSP_STIMEOUT_US = int(
    max(0.1, float(getattr(config, "RTSP_SOCKET_TIMEOUT_SECONDS", 5.0))) * 1_000_000
)
os.environ.setdefault(
    "OPENCV_FFMPEG_CAPTURE_OPTIONS",
    f"rtsp_transport;tcp|stimeout;{_RTSP_STIMEOUT_US}",
)


def _use_live_drain_path(source_url: str) -> bool:
    """
    Decide whether _run_frame_loop should use the live buffer-drain path
    (grab-all / retrieve-last + lag kill-valve) or the sequential file-playback
    path.

    V2 fix: live rtsp:// / rtsps:// sources MUST always drain, regardless of
    SIMULATE_REALTIME_PLAYBACK. Previously a stray SIMULATE_REALTIME_PLAYBACK=true
    (e.g. leaking in from .env.local) put live cameras on the sequential-read
    path, so FFmpeg/TCP buffers grew without bound until the process OOMed.
    The simulation branch is only meaningful for file/non-rtsp sources.
    """
    if (source_url or "").strip().lower().startswith(("rtsp://", "rtsps://")):
        return True
    return not config.SIMULATE_REALTIME_PLAYBACK

# L9 fix: cloudinary.config() was being called inside the upload thread for
# every debug frame. The library's config is a process-global, so re-setting
# it on every upload is wasted work and produces noisy debug output. Move
# the configuration call to module load and skip it entirely if the keys are
# not configured (debug uploads are disabled by default anyway).
try:
    if getattr(config, "CLOUDINARY_CLOUD_NAME", "") and getattr(config, "CLOUDINARY_API_KEY", "") and getattr(config, "CLOUDINARY_API_SECRET", ""):
        import cloudinary as _cloudinary_module
        _cloudinary_module.config(
            cloud_name=config.CLOUDINARY_CLOUD_NAME,
            api_key=config.CLOUDINARY_API_KEY,
            api_secret=config.CLOUDINARY_API_SECRET,
            secure=True,
        )
except Exception as _cloudinary_err:  # noqa: BLE001
    logger.debug("cloudinary not configured at module load: %s", _cloudinary_err)


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
        # Guard: at most one Cloudinary debug upload in-flight per camera.
        # Prevents unbounded thread buildup when uploads are slow or failing.
        self._debug_upload_lock = threading.Lock()

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
        old_whip_url = self._config.whip_url
        self._config = new_config

        # If the user toggled the frontend Power Button, start or stop FFmpeg dynamically
        if not old_is_streaming and new_config.is_streaming:
            logger.info("[%s] ⚡ Cloudflare live feed toggled ON. Starting FFmpeg...", self.camera_id)
            self._start_ffmpeg()
        elif old_is_streaming and not new_config.is_streaming:
            logger.info("[%s] 🔌 Cloudflare live feed toggled OFF. Stopping FFmpeg...", self.camera_id)
            self._stop_ffmpeg()
        elif (
            old_is_streaming
            and new_config.is_streaming
            and old_whip_url != new_config.whip_url
            and new_config.whip_url
        ):
            # M25 fix: previously the WHIP URL (including its bearer token)
            # was captured by ffmpeg argv on first start and never refreshed.
            # When Cloudflare rotated the token the ffmpeg child kept POSTing
            # with the stale credential until the next service restart. Detect
            # the URL change here and tear down + relaunch ffmpeg so a token
            # rotation propagates within one config-poll cycle (≤60s).
            logger.info(
                "[%s] 🔄 WHIP URL changed (likely token rotation); restarting FFmpeg.",
                self.camera_id,
            )
            self._stop_ffmpeg()
            self._start_ffmpeg()

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
        # V3 fix: max_reconnect_attempts <= 0 means "retry forever" (with the
        # backoff capped at RECONNECT_MAX_DELAY). This is the new default —
        # a camera that is offline for an hour used to permanently kill its
        # capture loop after 10 attempts (~10 min) and never recover until
        # a config change happened to trigger reconcile().
        max_attempts = self._state.max_reconnect_attempts
        unlimited = max_attempts <= 0

        while not self._stop_event.is_set():
            logger.info(
                "[%s] Connecting to RTSP (attempt %d/%s)...",
                self._config.camera_id, attempt + 1,
                "∞" if unlimited else max_attempts,
            )

            with self._state_lock:
                self._state.status = "connecting" if attempt == 0 else "reconnecting"
                if attempt > 0:
                    self._state.reconnect_attempts += 1
                    self._state.last_reconnect_at = datetime.now(timezone.utc)

            try:
                # H1 fix: OPENCV_FFMPEG_CAPTURE_OPTIONS is set once at module
                # load; do NOT mutate the process-global per reconnect.
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
            if not unlimited and attempt >= max_attempts:
                logger.error(
                    "[%s] ❌ Max reconnect attempts (%d) reached. Stream entering error state.",
                    self._config.camera_id, max_attempts
                )
                with self._state_lock:
                    self._state.status = "error"
                return None

            # Exponential backoff delay, capped at RECONNECT_MAX_DELAY.
            # The exponent is clamped so unlimited retry mode can't compute
            # astronomically large intermediate ints (2**100000).
            delay = min(
                self.RECONNECT_BASE_DELAY * (2 ** min(attempt - 1, 16)),
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

        # C7 fix: validate the WHIP URL host against an explicit allow-list
        # before we attach our long-lived Cloudflare API token to the request.
        # A tampered DB row that swaps in attacker.example.com would otherwise
        # exfiltrate the bearer.
        try:
            whip_host = (urlparse(self._config.whip_url).hostname or "").lower()
        except Exception:  # noqa: BLE001
            whip_host = ""
        allowed = tuple(getattr(config, "CLOUDFLARE_WHIP_ALLOWED_HOSTS", ()) or ())
        if allowed and not any(whip_host == h or whip_host.endswith("." + h) for h in allowed):
            logger.error(
                "[%s] Refusing to start FFmpeg WHIP publisher: host '%s' is not in CLOUDFLARE_WHIP_ALLOWED_HOSTS (%s)",
                self._config.camera_id, whip_host, ",".join(allowed),
            )
            with self._state_lock:
                self._state.last_error = f"WHIP host '{whip_host}' not allowed"
            return

        bearer = (config.CLOUDFLARE_API_TOKEN or "").strip()
        if not bearer:
            logger.error(
                "[%s] CLOUDFLARE_API_TOKEN is not configured; cannot publish via WHIP.",
                self._config.camera_id,
            )
            return

        logger.info("[%s] 🎥 Starting FFmpeg WebRTC publisher...", self._config.camera_id)
        # NOTE on token leakage: FFmpeg's WHIP muxer accepts the bearer only
        # via the `-headers` option which lands in argv. On Linux the cmdline
        # is only readable by the process owner / root, but anyone with
        # `ps`/container exec access can still see it. Residual mitigations:
        #  * host allow-list above (token can't be exfiltrated to attacker URL)
        #  * stderr log redaction below (token can't leak via our log pipeline)
        #  * use short-lived rotating Cloudflare tokens upstream (recommended)
        # M21 fix: encoder knobs are pulled from config so operators can
        # tune CPU vs quality without a rebuild. Defaults reproduce the
        # previous hard-coded values for backward compatibility.
        cmd = [
            "ffmpeg",
            "-hide_banner", "-loglevel", "error",
            "-rtsp_transport", "tcp",
            "-i", self._config.rtsp_url,
            "-f", "lavfi", "-i", "anullsrc=r=44100:cl=stereo",
            "-c:v", str(config.WHIP_VIDEO_CODEC),
            "-preset", str(config.WHIP_VIDEO_PRESET),
            "-tune", str(config.WHIP_VIDEO_TUNE),
            "-profile:v", str(config.WHIP_VIDEO_PROFILE),
            "-level", str(config.WHIP_VIDEO_LEVEL),
            "-pix_fmt", "yuv420p",
            "-r", str(int(config.WHIP_FRAMERATE)),
            "-bf", "0",
            "-g", str(int(config.WHIP_GOP_SIZE)),
            # Encode the dummy audio (WebRTC strictly requires Opus)
            "-c:a", str(config.WHIP_AUDIO_CODEC), "-b:a", str(config.WHIP_AUDIO_BITRATE),
            # FFmpeg WHIP muxer requires explicit HTTP line endings for custom headers
            "-headers", f"Authorization: Bearer {bearer}\r\n",
            "-f", "whip",
            "-tls_verify", "0",
            self._config.whip_url
        ]
        
        try:
            # We use a list instead of a shell string for safer execution in Docker
            proc = subprocess.Popen(
                cmd,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.PIPE,
                stdin=subprocess.DEVNULL,
                shell=False # Set to False for Docker/Linux stability
            )
            self._ffmpeg_process = proc

            # Log stderr in background so we can see the REAL error if it crashes again.
            # Race fix: bind the Popen object into the thread closure as a local.
            # Reading self._ffmpeg_process from the thread raced with
            # _stop_ffmpeg() setting it to None (AttributeError on .stderr) or,
            # worse, with a restart swapping in a NEW process whose stderr this
            # stale thread would then consume.
            def log_stderr(p=proc, token: str = bearer):
                if p.stderr:
                    for line in p.stderr:
                        msg = line.decode('utf-8', errors='replace').strip()
                        # C7 fix: ensure the bearer never lands in our log pipeline
                        if token and token in msg:
                            msg = msg.replace(token, "***REDACTED***")
                        logger.error("[%s] FFMPEG ERROR: %s", self._config.camera_id, msg)

            threading.Thread(target=log_stderr, daemon=True).start()
            logger.info("[%s] 🎥 FFmpeg published started (PID: %d)", self._config.camera_id, proc.pid)
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

        # V2 fix: the drain path is decided by the SOURCE, not by the global
        # SIMULATE_REALTIME_PLAYBACK flag. Live rtsp:// sources always drain;
        # only file/non-rtsp sources may take the sequential simulation path.
        use_drain_path = _use_live_drain_path(self._config.rtsp_url)
        # File-playback pacing: sleep 1/source_fps per decoded frame so an MP4
        # plays back at real-time speed instead of being chewed through as fast
        # as the CPU allows. Only used on the simulation (non-drain) path.
        playback_delay = 0.0 if use_drain_path else (1.0 / source_fps)

        sampling_modulo = max(1, int(source_fps / target_fps))

        while not self._stop_event.is_set():
            # FOR LIVE RTSP: Drain the buffer to eliminate lag
            # We grab all waiting frames but only retrieve/process the last one
            if use_drain_path:
                grab_count = 0
                # H7 fix: use the source FPS we already negotiated rather than
                # assuming 30 fps. 60 fps cameras used to trip the kill-valve at
                # half the intended lag; 8-15 fps cameras under-tripped and
                # accumulated huge backlog before reconnecting.
                max_lag_frames = max(1, int(config.MAX_STREAM_LAG_SECONDS * source_fps))
                # C1 fix: initialise `now` so it is bound on every path. With the
                # previous code `now` was only assigned inside the drain loop;
                # if cap.grab() returned False on the first iteration we still
                # later executed `last_process_time = now`, raising NameError.
                now = time.monotonic()
                ret = False
                frame = None
                
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
                
                # C3 fix: read status under the same lock that mutates it.
                # Otherwise a watchdog-triggered status flip can race the
                # comparison and we either miss the reconnect or read a torn
                # string value.
                with self._state_lock:
                    is_reconnecting = self._state.status == "reconnecting"
                if is_reconnecting:
                    break # Force outer loop to reconnect

                if not grabbed or not ret:
                    time.sleep(0.01)
                    continue
            else:
                # FOR SIMULATION (file / non-rtsp sources only): sequential read
                ret, frame = cap.read()
                video_frame_count += 1
                # C1 fix: in simulate-realtime mode the live-branch drain loop
                # never runs, so `now` must be set here for the later
                # `last_process_time = now` line. The previous code raised
                # NameError on the first frame of any MP4 / file-based test run.
                now = time.monotonic()

                if not ret or frame is None:
                    logger.warning("[%s] cap.read() returned no frame — stream may have ended at frame %d",
                                   self._config.camera_id, video_frame_count)
                    break

                # V2 fix: pace file playback at the source frame rate so
                # "simulate realtime" actually simulates real time instead of
                # decoding the file as fast as possible.
                if playback_delay > 0:
                    time.sleep(playback_delay)

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
            # C6 fix: this is a diagnostic aid that, when left on in production
            # with many cameras, burns the Cloudinary free tier in hours and
            # spawns unbounded daemon threads on network stalls. Gated behind
            # ``ENABLE_DEBUG_FRAME_UPLOADS`` (default False).
            # Runs in a daemon thread so the blocking HTTP upload never stalls the drain loop.
            now_wall = time.monotonic()
            if getattr(config, "ENABLE_DEBUG_FRAME_UPLOADS", False) and now_wall - last_debug_upload_time >= 30.0:
                last_debug_upload_time = now_wall
                import cv2 as _cv2
                _ok, jpg_bytes = _cv2.imencode(".jpg", frame)
                if not _ok:
                    logger.warning("cv2.imencode failed for camera %s; skipping debug upload.", self._config.camera_id)
                    last_debug_upload_time = now_wall  # reset timer to avoid a tight retry loop
                else:
                    _upload_bytes = jpg_bytes.tobytes()
                    _camera_id    = self._config.camera_id

                    if not self._debug_upload_lock.acquire(blocking=False):
                        logger.debug("[%s] Debug upload already in-flight; skipping this frame.", _camera_id)
                    else:
                        _lock = self._debug_upload_lock

                        def _cloudinary_upload(raw: bytes, cam_id: str) -> None:
                            try:
                                # L9 fix: cloudinary.config() was previously
                                # invoked here on every upload — the library's
                                # config is a process-global so this is wasted
                                # work. The module-level config call at the
                                # top of this file now handles initialisation.
                                import cloudinary.uploader
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
                            finally:
                                _lock.release()

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
