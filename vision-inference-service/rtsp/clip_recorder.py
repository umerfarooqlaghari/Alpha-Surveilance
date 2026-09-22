"""
rtsp/clip_recorder.py
Asynchronously records, annotates, encodes, and uploads 2-3 second MP4 violation video clips.
"""
import os
import cv2
import time
import shutil
import logging
import asyncio
import tempfile
import threading
import subprocess
import numpy as np
from datetime import datetime, timezone
from concurrent.futures import ThreadPoolExecutor
from typing import List, Optional, Any, Tuple

from .video_buffer import RollingFrameBuffer
import config
import metrics as vision_metrics

logger = logging.getLogger(__name__)

# A clip is only uploaded when the buffer actually held this fraction of the
# requested pre/post window. Below it the footage is not the violation.
MIN_WINDOW_COVERAGE = 0.5
MIN_CLIP_FRAMES = 8
# The detection box is a single-instant measurement. Drawing it across a 3s clip
# makes it drift off a moving subject, so it is only drawn on frames this close
# to the trigger (audit P3).
BOX_ANNOTATION_TOLERANCE_SEC = 0.35


def _draw_annotation_on_frame(
    frame: np.ndarray,
    box: dict,
    label: str,
    score: float,
    track_id: Optional[int] = None,
    orig_size: Optional[tuple] = None,
) -> np.ndarray:
    """
    Draws bounding box and label tag on a video frame.
    Coordinates are scaled if frame dimensions differ from inference canvas.
    """
    if not isinstance(box, dict):
        return frame

    h, w = frame.shape[:2]
    try:
        xmin = float(box.get("xmin", 0))
        ymin = float(box.get("ymin", 0))
        xmax = float(box.get("xmax", w))
        ymax = float(box.get("ymax", h))
    except (TypeError, ValueError):
        return frame

    if orig_size and orig_size[0] > 0 and orig_size[1] > 0:
        orig_w, orig_h = orig_size
        scale_x = w / orig_w
        scale_y = h / orig_h
        xmin *= scale_x
        ymin *= scale_y
        xmax *= scale_x
        ymax *= scale_y

    xmin = max(0, min(w - 2, int(xmin)))
    ymin = max(0, min(h - 2, int(ymin)))
    xmax = max(xmin + 1, min(w - 1, int(xmax)))
    ymax = max(ymin + 1, min(h - 1, int(ymax)))

    # Box color: Red (BGR: 0, 0, 240) for active violation
    color = (0, 0, 240)
    thickness = max(2, int(min(w, h) / 360))

    # Draw bounding box
    cv2.rectangle(frame, (xmin, ymin), (xmax, ymax), color, thickness)

    # Label text banner
    display_id = f"ID:{track_id} " if track_id is not None else ""
    text = f"{display_id}{label} {score:.0%}"
    font = cv2.FONT_HERSHEY_SIMPLEX
    font_scale = max(0.45, min(w, h) / 1200.0)
    font_thickness = max(1, int(font_scale * 2))

    (text_w, text_h), baseline = cv2.getTextSize(text, font, font_scale, font_thickness)
    banner_ymin = max(0, ymin - text_h - 6)
    banner_ymax = ymin

    # Draw filled background banner for text readability
    cv2.rectangle(
        frame,
        (xmin, banner_ymin),
        (min(w - 1, xmin + text_w + 8), banner_ymax),
        color,
        -1,
    )
    # White text inside banner
    cv2.putText(
        frame,
        text,
        (xmin + 4, banner_ymax - 3),
        font,
        font_scale,
        (255, 255, 255),
        font_thickness,
        cv2.LINE_AA,
    )
    return frame


def _draw_time_offset_tag(frame: np.ndarray, offset_sec: float, label: str) -> np.ndarray:
    """
    Stamps a small corner tag showing the frame's offset from the violation
    instant (``T+0.00s``). Because the bounding box is only drawn near the
    trigger, this is what tells a reviewer which frame is the actual evidence
    and which are context.
    """
    h, w = frame.shape[:2]
    font = cv2.FONT_HERSHEY_SIMPLEX
    font_scale = max(0.4, min(w, h) / 1400.0)
    thickness = max(1, int(font_scale * 2))
    text = f"{label}  T{offset_sec:+.2f}s"
    (text_w, text_h), _ = cv2.getTextSize(text, font, font_scale, thickness)
    pad = max(3, int(text_h * 0.4))
    cv2.rectangle(frame, (0, 0), (min(w - 1, text_w + pad * 2), text_h + pad * 2), (0, 0, 0), -1)
    cv2.putText(
        frame, text, (pad, text_h + pad // 2), font, font_scale,
        (255, 255, 255), thickness, cv2.LINE_AA,
    )
    return frame


class ViolationClipRecorder:
    """
    Manages capturing 2-3s pre/post event video clips when violations occur.
    """

    def __init__(
        self,
        s3_client: Any,
        api_client: Any,
        event_loop: asyncio.AbstractEventLoop,
        max_workers: Optional[int] = None,
        max_inflight: Optional[int] = None,
    ):
        self.s3_client = s3_client
        self.api_client = api_client
        self.loop = event_loop
        workers = int(max_workers if max_workers is not None else getattr(config, "CLIP_WORKERS", 4))
        self._executor = ThreadPoolExecutor(
            max_workers=max(1, workers),
            thread_name_prefix="clip_recorder",
        )
        # Audit P2: ThreadPoolExecutor's queue is unbounded. Under a burst the
        # queued jobs used to run long after their frames had aged out of the
        # 4.5s buffer. We cap in-flight work and shed load explicitly instead.
        self._max_inflight = max(1, int(
            max_inflight if max_inflight is not None else getattr(config, "CLIP_MAX_INFLIGHT", 12)
        ))
        self._inflight = 0
        self._inflight_lock = threading.Lock()
        self._closed = False
        self._ffmpeg_path = shutil.which("ffmpeg") or "ffmpeg"

    # ── lifecycle ────────────────────────────────────────────────────────────
    def shutdown(self, wait: bool = False, timeout: Optional[float] = None) -> None:
        """
        Stops accepting new clips and tears the worker pool down.

        Audit P3: main.py previously reached into the private ``_executor`` with
        ``cancel_futures=True``, silently discarding queued clips on every deploy.
        """
        self._closed = True
        try:
            if wait and timeout:
                # Give in-flight encodes a bounded chance to finish.
                deadline = time.monotonic() + timeout
                while time.monotonic() < deadline:
                    with self._inflight_lock:
                        if self._inflight == 0:
                            break
                    time.sleep(0.1)
            self._executor.shutdown(wait=wait, cancel_futures=not wait)
        except Exception:  # noqa: BLE001
            logger.exception("Clip recorder shutdown failed")

    def _acquire_slot(self) -> bool:
        with self._inflight_lock:
            if self._closed or self._inflight >= self._max_inflight:
                return False
            self._inflight += 1
            n = self._inflight
        try:
            vision_metrics.clip_inflight.set(n)
        except Exception:  # noqa: BLE001
            pass
        return True

    def _release_slot(self) -> None:
        with self._inflight_lock:
            self._inflight = max(0, self._inflight - 1)
            n = self._inflight
        try:
            vision_metrics.clip_inflight.set(n)
        except Exception:  # noqa: BLE001
            pass

    @staticmethod
    def _count(outcome: str) -> None:
        try:
            vision_metrics.clip_total.labels(outcome=outcome).inc()
        except Exception:  # noqa: BLE001
            pass

    # ── dispatch ─────────────────────────────────────────────────────────────
    def record_violation_clip_async(
        self,
        frame_buffer: RollingFrameBuffer,
        violation_id: str,
        camera_id: str,
        tenant_id: str,
        trigger_time: float,
        track_id: Optional[int],
        detection: Optional[dict],
        orig_frame_size: Optional[tuple] = None,
        pre_roll_sec: Optional[float] = None,
        post_roll_sec: Optional[float] = None,
    ) -> bool:
        """
        Dispatches clip extraction and encoding to the background worker pool.
        Never blocks the RTSP capture thread. Returns False when the job was shed
        because the pool is saturated (recording it late would capture the wrong
        footage, so dropping it is the correct outcome).
        """
        if not frame_buffer or self._closed:
            return False

        if pre_roll_sec is None:
            pre_roll_sec = float(getattr(config, "CLIP_PRE_ROLL_SECONDS", 1.5))
        if post_roll_sec is None:
            post_roll_sec = float(getattr(config, "CLIP_POST_ROLL_SECONDS", 1.5))

        if not self._acquire_slot():
            self._count("rejected_saturated")
            logger.warning(
                "[%s] Clip recorder saturated (%d in flight); dropping clip for %s",
                camera_id, self._max_inflight, violation_id,
            )
            return False

        try:
            self._executor.submit(
                self._process_clip_worker,
                frame_buffer,
                violation_id,
                camera_id,
                tenant_id,
                trigger_time,
                track_id,
                detection,
                orig_frame_size,
                pre_roll_sec,
                post_roll_sec,
            )
        except RuntimeError:
            # Pool already shut down.
            self._release_slot()
            return False
        return True

    # ── worker ───────────────────────────────────────────────────────────────
    def _process_clip_worker(
        self,
        frame_buffer: RollingFrameBuffer,
        violation_id: str,
        camera_id: str,
        tenant_id: str,
        trigger_time: float,
        track_id: Optional[int],
        detection: Optional[dict],
        orig_frame_size: Optional[tuple],
        pre_roll_sec: float,
        post_roll_sec: float,
    ) -> None:
        started = time.monotonic()
        try:
            # 1. Wait for post-roll frames to accumulate in the rolling buffer
            elapsed = time.monotonic() - trigger_time
            remaining_wait = max(0.0, post_roll_sec - elapsed)
            if remaining_wait > 0:
                time.sleep(remaining_wait + 0.05)

            # 2. Extract buffered frames covering [trigger - pre_roll, trigger + post_roll].
            #    Audit P1: get_window is now strict. If the window has aged out of
            #    the buffer — slow re-id, a saturated pool, a stream reconnect that
            #    cleared the buffer — we abandon the clip rather than upload
            #    unrelated footage stamped with this violation's bounding box.
            start_ts = trigger_time - pre_roll_sec
            end_ts = trigger_time + post_roll_sec
            buffered_frames = frame_buffer.get_window(start_ts, end_ts)
            coverage = frame_buffer.coverage(start_ts, end_ts)

            if len(buffered_frames) < MIN_CLIP_FRAMES or coverage < MIN_WINDOW_COVERAGE:
                self._count("insufficient_frames")
                logger.warning(
                    "[%s] Skipping violation clip %s — buffer held %d frame(s), %.0f%% of the "
                    "requested %.1fs window (age at capture: %.2fs). Not uploading unrelated footage.",
                    camera_id, violation_id, len(buffered_frames), coverage * 100.0,
                    pre_roll_sec + post_roll_sec, elapsed,
                )
                return

            clip_fps = float(frame_buffer.target_fps) if frame_buffer.target_fps > 0 else 15.0

            # 3-6. Annotate, encode, upload, patch — shared with the /analyze path.
            self._annotate_encode_upload_patch(
                buffered_frames, trigger_time, clip_fps, violation_id, camera_id,
                tenant_id, track_id, detection, orig_frame_size,
            )

        except Exception:  # noqa: BLE001
            logger.exception("[%s] Unexpected error in violation clip recorder worker", camera_id)
        finally:
            self._release_slot()
            try:
                vision_metrics.clip_duration_seconds.observe(time.monotonic() - started)
            except Exception:  # noqa: BLE001
                pass

    # ── shared clip production ───────────────────────────────────────────────
    def _annotate_encode_upload_patch(
        self,
        frames_with_ts: List[Tuple[float, np.ndarray]],
        trigger_time: float,
        clip_fps: float,
        violation_id: str,
        camera_id: str,
        tenant_id: str,
        track_id: Optional[int],
        detection: Optional[dict],
        orig_frame_size: Optional[tuple],
    ) -> bool:
        """
        Annotates a window of frames, encodes it, uploads it and attaches the URL
        to the violation.

        ``trigger_time`` and the frame timestamps only have to share a clock and
        an origin — the live path uses ``time.monotonic()``, /analyze uses media
        time from the decoded file. Everything downstream is clock-agnostic.
        """
        # The box is a single-instant measurement, so draw it only near the
        # trigger; every frame gets a T+/-offset tag so a reviewer can tell
        # evidence from context.
        label = "violation"
        if detection:
            label = str(detection.get("label", "violation"))
        box = detection.get("box") if isinstance(detection, dict) else None
        score = 1.0
        if isinstance(detection, dict):
            try:
                score = float(detection.get("score", 1.0))
            except (TypeError, ValueError):
                score = 1.0

        frames: List[np.ndarray] = []
        for ts, frame in frames_with_ts:
            offset = ts - trigger_time
            if isinstance(box, dict) and abs(offset) <= BOX_ANNOTATION_TOLERANCE_SEC:
                _draw_annotation_on_frame(
                    frame, box=box, label=label, score=score,
                    track_id=track_id, orig_size=orig_frame_size,
                )
            _draw_time_offset_tag(frame, offset, label)
            frames.append(frame)

        mp4_bytes = self._encode_frames_to_h264(frames, fps=clip_fps)
        if not mp4_bytes:
            self._count("encode_fail")
            logger.warning("[%s] Failed to encode violation clip MP4 for %s", camera_id, violation_id)
            return False

        if not self.s3_client or not getattr(config, "S3_BUCKET_NAME", None):
            logger.debug("[%s] S3 not configured; skipping clip upload for %s", camera_id, violation_id)
            return False

        date_str = datetime.now(timezone.utc).strftime("%Y-%m-%d")
        s3_key = (
            f"violations/{_sanitise_key_segment(tenant_id)}/"
            f"{_sanitise_key_segment(camera_id)}/{date_str}/{violation_id}.mp4"
        )
        clip_url = f"https://{config.S3_BUCKET_NAME}.s3.{config.AWS_REGION}.amazonaws.com/{s3_key}"

        put_kwargs = {
            "Bucket": config.S3_BUCKET_NAME,
            "Key": s3_key,
            "Body": mp4_bytes,
            "ContentType": "video/mp4",
            # Surveillance footage of identifiable people: encrypted at rest, and
            # tagged so an S3 lifecycle rule can expire clips independently of
            # the still frames.
            "Tagging": f"retention=violation-clip&retention-days={getattr(config, 'CLIP_RETENTION_DAYS', 90)}",
        }
        kms_key = (getattr(config, "CLIP_SSE_KMS_KEY_ID", "") or "").strip()
        if kms_key:
            put_kwargs["ServerSideEncryption"] = "aws:kms"
            put_kwargs["SSEKMSKeyId"] = kms_key
        elif getattr(config, "CLIP_SSE_ALGORITHM", ""):
            put_kwargs["ServerSideEncryption"] = config.CLIP_SSE_ALGORITHM

        try:
            self.s3_client.put_object(**put_kwargs)
            logger.info("[%s] 🎬 Violation video clip uploaded to S3: %s", camera_id, s3_key)
        except Exception as s3_err:  # noqa: BLE001
            self._count("upload_fail")
            logger.warning("[%s] S3 video clip upload failed: %s", camera_id, s3_err)
            return False

        self._patch_clip_url(violation_id, camera_id, clip_url)
        return True

    def record_clip_from_frames_async(
        self,
        frames_with_ts: List[Tuple[float, np.ndarray]],
        violation_id: str,
        camera_id: str,
        tenant_id: str,
        trigger_time: float,
        track_id: Optional[int] = None,
        detection: Optional[dict] = None,
        orig_frame_size: Optional[tuple] = None,
        clip_fps: float = 15.0,
    ) -> bool:
        """
        Produces a clip from frames the caller has ALREADY collected.

        Used by /analyze, where frames come from a decoded file rather than a
        live rolling buffer: there is nothing to wait for, so this skips the
        post-roll sleep and the buffer-coverage check that only make sense for a
        live stream. Load-shedding still applies — a saturated pool means the
        clip is dropped rather than queued behind stale work.
        """
        if self._closed or not frames_with_ts:
            return False
        if not self._acquire_slot():
            self._count("rejected_saturated")
            logger.warning(
                "[%s] Clip recorder saturated; dropping analyze clip for %s",
                camera_id, violation_id,
            )
            return False

        def _work():
            started = time.monotonic()
            try:
                self._annotate_encode_upload_patch(
                    frames_with_ts, trigger_time, clip_fps, violation_id,
                    camera_id, tenant_id, track_id, detection, orig_frame_size,
                )
            except Exception:  # noqa: BLE001
                logger.exception("[%s] analyze clip worker failed for %s", camera_id, violation_id)
            finally:
                self._release_slot()
                try:
                    vision_metrics.clip_duration_seconds.observe(time.monotonic() - started)
                except Exception:  # noqa: BLE001
                    pass

        try:
            self._executor.submit(_work)
        except RuntimeError:
            self._release_slot()
            return False
        return True

    # ── persistence ──────────────────────────────────────────────────────────
    def _patch_clip_url(self, violation_id: str, camera_id: str, clip_url: str) -> None:
        """
        Schedules the VideoClipPath PATCH on the main event loop with bounded
        retries.

        Audit P2: the clip is dispatched alongside violation creation, so the
        PATCH can reach the API before the row exists (the POST may be sitting in
        the DLQ during a rolling restart). A single fire-and-forget attempt lost
        the URL permanently and left an orphaned S3 object. Retrying on the loop
        (not in a worker thread) keeps the pool free during backoff.
        """
        if not self.api_client or not self.loop or not self.loop.is_running():
            self._count("patch_fail")
            logger.warning("[%s] No running event loop; VideoClipPath lost for %s", camera_id, violation_id)
            return

        attempts = max(1, int(getattr(config, "CLIP_PATCH_MAX_ATTEMPTS", 5)))
        base_delay = float(getattr(config, "CLIP_PATCH_BASE_DELAY_SECONDS", 2.0))

        async def _patch_with_retry() -> bool:
            for attempt in range(1, attempts + 1):
                try:
                    ok = await self.api_client.update_violation(
                        violation_id=violation_id,
                        video_clip_path=clip_url,
                    )
                except Exception:  # noqa: BLE001
                    logger.exception(
                        "[%s] VideoClipPath PATCH raised (attempt %d/%d) for %s",
                        camera_id, attempt, attempts, violation_id,
                    )
                    ok = False
                if ok:
                    if attempt > 1:
                        logger.info(
                            "[%s] VideoClipPath persisted for %s on attempt %d",
                            camera_id, violation_id, attempt,
                        )
                    return True
                if attempt < attempts:
                    await asyncio.sleep(base_delay * (2 ** (attempt - 1)))
            return False

        try:
            fut = asyncio.run_coroutine_threadsafe(_patch_with_retry(), self.loop)
        except RuntimeError:
            self._count("patch_fail")
            logger.warning("[%s] Event loop closed; VideoClipPath lost for %s", camera_id, violation_id)
            return

        def _done(f, v_id=violation_id, c_id=camera_id, key=clip_url):
            # Audit P2: the old callback logged success unconditionally and never
            # inspected the future, so a failed PATCH read as "patched".
            try:
                succeeded = f.result()
            except Exception:  # noqa: BLE001
                logger.exception("[%s] VideoClipPath PATCH task crashed for %s", c_id, v_id)
                self._count("patch_fail")
                return
            if succeeded:
                self._count("uploaded")
                logger.debug("[%s] VideoClipPath patched for %s", c_id, v_id)
            else:
                self._count("patch_fail")
                logger.error(
                    "[%s] VideoClipPath PATCH failed after %d attempts for violation %s; "
                    "clip is uploaded but orphaned at %s",
                    c_id, attempts, v_id, key,
                )

        fut.add_done_callback(_done)

    # ── encoding ─────────────────────────────────────────────────────────────
    def _encode_frames_to_h264(self, frames: List[np.ndarray], fps: float = 15.0) -> Optional[bytes]:
        """
        Encodes a sequence of BGR frames into an H.264 MP4 file with +faststart
        using FFmpeg CLI for maximum browser compatibility.

        Audit P0: this used to call ``proc.stdin.close()`` and then
        ``proc.communicate(timeout=...)``. CPython's ``_communicate`` flushes
        stdin itself and catches only ``BrokenPipeError``, so the already-closed
        pipe raised ``ValueError: flush of closed file`` on every single call.
        The broad ``except Exception`` swallowed it and returned None — ffmpeg had
        in fact exited 0 and written a perfectly valid MP4, which was discarded.
        The feature therefore produced zero clips in production.

        The fix drops ``communicate()`` entirely: stderr goes to a temp file (so a
        chatty ffmpeg can never fill a 64KB pipe and deadlock the writer) and we
        close stdin ourselves, then ``wait()`` on the process.
        """
        if not frames:
            return None

        h, w = frames[0].shape[:2]
        # H.264 requires even dimensions
        w = w if w % 2 == 0 else w - 1
        h = h if h % 2 == 0 else h - 1
        if w <= 0 or h <= 0:
            return None

        timeout = float(getattr(config, "CLIP_ENCODE_TIMEOUT_SECONDS", 15.0))

        with tempfile.NamedTemporaryFile(suffix=".mp4", delete=False) as tmp_file:
            tmp_path = tmp_file.name
        with tempfile.NamedTemporaryFile(suffix=".log", delete=False) as err_file:
            err_path = err_file.name

        proc = None
        try:
            cmd = [
                self._ffmpeg_path,
                "-y",
                "-f", "rawvideo",
                "-vcodec", "rawvideo",
                "-s", f"{w}x{h}",
                "-pix_fmt", "bgr24",
                "-r", str(round(fps, 2)),
                "-i", "pipe:0",
                "-c:v", "libx264",
                "-preset", "veryfast",
                "-crf", "23",
                "-pix_fmt", "yuv420p",
                "-movflags", "+faststart",
                tmp_path,
            ]

            with open(err_path, "wb") as err_fh:
                proc = subprocess.Popen(
                    cmd,
                    stdin=subprocess.PIPE,
                    stdout=subprocess.DEVNULL,
                    stderr=err_fh,
                )

                # Write raw BGR bytes into FFmpeg stdin, then close so ffmpeg sees EOF.
                try:
                    for f in frames:
                        if f.shape[:2] != (h, w):
                            f = cv2.resize(f, (w, h), interpolation=cv2.INTER_AREA)
                        proc.stdin.write(f.tobytes())
                except (BrokenPipeError, OSError) as pipe_err:
                    logger.warning("FFmpeg pipe broke while feeding frames: %s", pipe_err)
                finally:
                    try:
                        proc.stdin.close()
                    except (BrokenPipeError, OSError, ValueError):
                        pass

                returncode = proc.wait(timeout=timeout)

            if returncode != 0:
                stderr_tail = ""
                try:
                    with open(err_path, "rb") as fh:
                        stderr_tail = fh.read()[-2000:].decode("utf-8", errors="replace")
                except OSError:
                    pass
                logger.warning("FFmpeg video encoding failed (rc=%s): %s", returncode, stderr_tail)
                return None

            with open(tmp_path, "rb") as f_out:
                data = f_out.read()
            return data or None

        except subprocess.TimeoutExpired:
            if proc is not None:
                proc.kill()
                proc.wait()
            logger.warning("FFmpeg video encoding timed out after %.1fs", timeout)
            return None
        except Exception as err:  # noqa: BLE001
            logger.warning("Error encoding video clip: %s", err)
            return None
        finally:
            if proc is not None and proc.poll() is None:
                try:
                    proc.kill()
                    proc.wait()
                except Exception:  # noqa: BLE001
                    pass
            for path in (tmp_path, err_path):
                if path and os.path.exists(path):
                    try:
                        os.remove(path)
                    except OSError:
                        pass


def _sanitise_key_segment(value: Any) -> str:
    """
    Keeps tenant/camera identifiers from escaping their prefix in the S3 key.
    An empty or path-like value would otherwise produce keys such as
    ``violations//cam/...`` or traverse into another tenant's prefix.
    """
    text = str(value or "").strip().replace("\\", "/")
    text = "".join(ch for ch in text if ch.isalnum() or ch in "-_.")
    text = text.strip(".") or "unknown"
    return text[:128]
