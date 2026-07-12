"""
main.py — Vision Inference Service
====================================
Two decoupled sub-systems:

1. [RTSP STREAM ENGINE]  (production-grade)
   - Pulls active camera RTSP URLs from the Violation API at startup
   - Runs concurrent camera streams via CameraStreamManager
   - Watchdog, auto-reconnect, FPS throttle, frame timeout per camera
   - Pause / Resume control via API (zero-cost suspend in testing)
   - Hot-reload via POST /streams/reload

2. [ANALYZE ENDPOINT]  (original, kept for testing)
   - POST /analyze — upload a single frame, get detections back
   - Fully independent of the RTSP engine

TESTING_MODE=true (set in .env or injected by AppHost):
   - AI model runs locally (on-device, free)
   - ALL AWS calls (S3 upload, SQS send) are SKIPPED
   - Violations are logged to console instead
   - Streams can be paused/resumed via  POST /streams/pause|resume
   → Zero AWS cost during development
"""

import os
import io
import json
import copy
import uuid
import time
import tempfile
import logging
import asyncio
import cv2
import threading
import numpy as np
from datetime import datetime, timezone
from typing import Optional, List
from contextlib import asynccontextmanager

import boto3
from fastapi import FastAPI, UploadFile, File, Form, Header, HTTPException, Depends, Request
from fastapi.responses import Response
from fastapi.responses import HTMLResponse, JSONResponse
from transformers import pipeline
from PIL import Image, ImageDraw

import config  # central config file (reads .env + environment)
from rtsp import CameraStreamManager, ViolationApiClient, CameraConfig
from rtsp.violation_manager import ViolationManager
from rtsp.device_identity import get_device_identifier, register_device

# ───────────────────────────────────────────────────────────────────────────
# C5 fix: internal-api-key dependency for mutating + privileged endpoints.
# Mounted as a FastAPI ``Depends`` so it composes cleanly with existing
# handlers. /metrics and /health stay open since they're scraped by
# infrastructure that doesn't carry the shared secret.
# ───────────────────────────────────────────────────────────────────────────
async def require_internal_api_key(
    x_internal_api_key: Optional[str] = Header(default=None, alias="X-Internal-Api-Key"),
) -> None:
    """Reject the request unless the shared internal API key is presented.

    When ``config.REQUIRE_INTERNAL_API_KEY`` is False (typical for local dev
    in TESTING_MODE) the check is bypassed entirely. In production the
    expected key MUST be configured; an empty configured key fails closed.
    """
    if not config.REQUIRE_INTERNAL_API_KEY:
        return
    expected = (config.INTERNAL_API_KEY or "").strip()
    if not expected:
        # Defensive: an empty configured key would let everyone in if we used
        # a simple `==` check. Fail closed instead.
        raise HTTPException(status_code=503, detail="Internal API key is not configured")
    presented = (x_internal_api_key or "").strip()
    if presented != expected:
        raise HTTPException(status_code=401, detail="Invalid or missing X-Internal-Api-Key")


# C5 fix: SSRF guard for RTSP URLs that come from external HTTP requests.
# Cameras typically live on private IPs, so we cannot block RFC1918 ranges.
# We DO block link-local (incl. cloud IMDS at 169.254.169.254) and reject
# everything except rtsp/rtsps schemes.
_ALLOWED_RTSP_SCHEMES = {"rtsp", "rtsps"}
_BLOCKED_HOST_PREFIXES = ("169.254.",)  # AWS / GCP / Azure IMDS link-local

def _validate_safe_rtsp_url(url: str) -> Optional[str]:
    """Return an error message if the URL is unsafe to open, else None."""
    if not url or not isinstance(url, str):
        return "URL is required"
    try:
        from urllib.parse import urlparse
        parsed = urlparse(url.strip())
    except Exception:  # noqa: BLE001
        return "URL is malformed"
    scheme = (parsed.scheme or "").lower()
    if scheme not in _ALLOWED_RTSP_SCHEMES:
        return f"Only rtsp:// and rtsps:// schemes are allowed (got '{scheme}')"
    host = (parsed.hostname or "").lower()
    if not host:
        return "URL is missing a host"
    for blocked in _BLOCKED_HOST_PREFIXES:
        if host.startswith(blocked):
            return f"Host '{host}' is blocked (link-local / metadata range)"
    return None

# ─────────────────────────────────────────────────────────────────────────────
# Logging
# ─────────────────────────────────────────────────────────────────────────────
# Root level defaults to INFO (env-tunable via LOG_LEVEL=DEBUG|INFO|WARNING…).
# The previous hard-coded DEBUG turned every third-party library's debug spew
# on in production and filled disks on multi-camera deployments.
logging.basicConfig(
    level=getattr(logging, getattr(config, "LOG_LEVEL", "INFO"), logging.INFO),
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
)
# Suppress noisy httpx and aws logs
logging.getLogger("httpx").setLevel(logging.WARNING)
logging.getLogger("boto3").setLevel(logging.WARNING)
logging.getLogger("botocore").setLevel(logging.WARNING)
logger = logging.getLogger("vision-service")

# ─────────────────────────────────────────────────────────────────────────────
# AWS Clients — only created when NOT in testing mode
# ─────────────────────────────────────────────────────────────────────────────
s3_client = None
sqs_client = None

if not config.TESTING_MODE:
    if config.AWS_REGION:
        # Tight timeouts + single attempt: these clients are used from the
        # frame path (snapshot upload). Default boto3 behaviour (60s connect,
        # multiple retries) could stall a worker for minutes on an AWS blip.
        from botocore.config import Config as _BotoConfig
        _boto_cfg = _BotoConfig(
            connect_timeout=3,
            read_timeout=5,
            retries={"max_attempts": 1},
        )
        s3_client  = boto3.client("s3",  region_name=config.AWS_REGION, config=_boto_cfg)
        sqs_client = boto3.client("sqs", region_name=config.AWS_REGION, config=_boto_cfg)
    else:
        logger.warning("AWS_REGION not set — S3/SQS clients not initialised")
else:
    logger.warning("⚠️  TESTING MODE: All AWS (S3 / SQS) calls are DISABLED. No cloud costs.")

# ─────────────────────────────────────────────────────────────────────────────
# AI Model Registry & Data Collection
# ─────────────────────────────────────────────────────────────────────────────
from concurrent.futures import ThreadPoolExecutor, TimeoutError as FutureTimeoutError
from inference.inference_engine import InferenceEngine
from inference.face_recognizer import identify_person
from data_collector import DataCollector
from rules.evaluator import evaluate_violations
import metrics as vision_metrics

# Audit P3 #13: side-effect threadpool. `data_collector.save_event` does
# blocking disk I/O and is submitted here as fire-and-forget.  4 workers is
# plenty since none of the callers block on the result.
_side_effect_pool = ThreadPoolExecutor(max_workers=4, thread_name_prefix="side-effect")

# P-2 fix: a SEPARATE pool for ``identify_person`` (reid HTTP).  The capture
# thread blocks on ``result(timeout=3.0)`` to attach the employee id to the
# violation payload, so a 4-worker pool would queue for ~12s under load on a
# 10-camera deployment.  Sizing this pool to ``MAX_STREAM_WORKERS`` means
# every camera can have a reid request in flight without queuing.
_reid_pool = ThreadPoolExecutor(
    max_workers=max(8, getattr(config, "MAX_STREAM_WORKERS", 32)),
    thread_name_prefix="reid",
)

logger.info("Initializing Modular Inference Engine...")
inference_engine = InferenceEngine()
data_collector   = DataCollector() # Base path defaults to 'captured_data'
logger.info("✅ Inference Engine & Data Collector ready")


def _safe_collect(pil_image, results, camera_id, tenant_id):
    """Wrapper run on the side-effect pool so collector exceptions never bubble
    back to the capture thread."""
    try:
        data_collector.collect_inference_event(pil_image, results, camera_id, tenant_id)
    except Exception:  # noqa: BLE001
        logger.exception("[%s] data_collector failed (background)", camera_id)

# ─────────────────────────────────────────────────────────────────────────────
# RTSP engine state
# ─────────────────────────────────────────────────────────────────────────────
api_client: ViolationApiClient    = None
stream_manager: CameraStreamManager = None
main_loop: asyncio.AbstractEventLoop = None

# Edge-device scoping: set during lifespan startup after successful registration.
# When None the vision service polls "all active cameras" (legacy single-device
# behaviour) so existing dev / local-only deployments keep working unchanged.
edge_device_id: Optional[str] = None

# Signature of the latest camera/rule config applied to stream_manager.
_last_config_signature: Optional[tuple] = None

# Global pause flag — when True, on_frame() is a no-op even if streams keep reading
_streams_paused: bool = False

# Post-time dedupe cache for the same subject repeatedly triggering the same SOP
# on the same camera in a short window. Keyed by camera+sop+identity.
_identity_dedupe_lock = threading.Lock()
_identity_last_post: dict[tuple, float] = {}


def _apply_config(cameras: list) -> list:
    """Normalise camera config from the API.

    * Only fall back to global TARGET_FPS when the per-camera value is missing
      or non-positive.
    * L4 fix: clamp target_fps to a sane upper bound so a bad API response
      (e.g. ``target_fps=1000``) can't pin a capture thread at 100% CPU.
    * M11 fix: only fall back to the global FRAME_TIMEOUT_SECONDS when the
      per-camera value is missing. Previously we unconditionally overwrote
      the field, silently discarding any per-camera override the API sent.
    * L7 fix: defensively strip whitespace from string fields so an API
      payload with stray newlines or trailing spaces in camera_id, name,
      location, rtsp_url, or whip_url doesn't silently mismatch hot-reload
      signatures or fail RTSP probing.
    """
    max_fps = float(getattr(config, "MAX_TARGET_FPS", 30.0))
    for cam in cameras:
        # L7 fix: normalise string fields once, here.
        for attr in ("camera_id", "camera_db_id", "tenant_id", "tenant_name",
                     "rtsp_url", "whip_url", "name", "location"):
            val = getattr(cam, attr, None)
            if isinstance(val, str):
                setattr(cam, attr, val.strip())

        if not getattr(cam, "target_fps", None) or cam.target_fps <= 0:
            cam.target_fps = config.TARGET_FPS
        if cam.target_fps > max_fps:
            logger.warning(
                "[%s] target_fps=%.2f exceeds MAX_TARGET_FPS=%.2f; clamping.",
                getattr(cam, "camera_id", "?"), cam.target_fps, max_fps,
            )
            cam.target_fps = max_fps
        existing_timeout = getattr(cam, "frame_timeout_seconds", None)
        if not existing_timeout or existing_timeout <= 0:
            cam.frame_timeout_seconds = config.FRAME_TIMEOUT_SECONDS
    return cameras


def _identity_dedupe_key(
    camera_db_id: str,
    sop_violation_type_id: Optional[str],
    employee_id: Optional[str],
    unknown_person_id: Optional[str],
) -> Optional[tuple]:
    if employee_id:
        identity = ("employee", str(employee_id).strip().lower())
    elif unknown_person_id:
        identity = ("unknown", str(unknown_person_id).strip().lower())
    else:
        return None
    return (str(camera_db_id), str(sop_violation_type_id or ""), identity[0], identity[1])


def _identity_recently_posted(key: Optional[tuple], now_ts: float) -> bool:
    if key is None:
        return False
    window = float(getattr(config, "IDENTITY_DEDUP_SECONDS", 0.0))
    if window <= 0:
        return False
    with _identity_dedupe_lock:
        last = _identity_last_post.get(key)
        if last is not None and (now_ts - last) < window:
            return True
        _identity_last_post[key] = now_ts

        # Opportunistic cleanup so this dict remains bounded.
        cutoff = now_ts - max(300.0, window * 3.0)
        stale_keys = [k for k, t in _identity_last_post.items() if t < cutoff]
        for k in stale_keys:
            _identity_last_post.pop(k, None)
    return False


def _rule_signature(rule) -> tuple:
    labels = tuple(sorted(getattr(rule, "trigger_labels", []) or []))
    rule_cfg = getattr(rule, "rule_config", {}) or {}
    try:
        cfg_sig = json.dumps(rule_cfg, sort_keys=True, separators=(",", ":"))
    except Exception:  # noqa: BLE001
        cfg_sig = str(rule_cfg)
    return (
        str(getattr(rule, "sop_violation_type_id", "")),
        str(getattr(rule, "model_identifier", "")),
        labels,
        cfg_sig,
    )


def _camera_signature(cam: CameraConfig) -> tuple:
    schedules = tuple(
        sorted(
            (
                str(getattr(s, "start_time", "")),
                str(getattr(s, "end_time", "")),
                int(getattr(s, "days_of_week", 127)),
                bool(getattr(s, "is_active", True)),
                str(getattr(s, "label", "")),
            )
            for s in (getattr(cam, "detection_schedules", []) or [])
        )
    )
    rules = tuple(sorted(_rule_signature(r) for r in (getattr(cam, "violation_rules", []) or [])))
    return (
        str(cam.camera_id),
        str(cam.rtsp_url),
        bool(getattr(cam, "is_streaming", False)),
        bool(getattr(cam, "is_detection_enabled", True)),
        round(float(getattr(cam, "target_fps", 0.0)), 3),
        rules,
        schedules,
    )


def _camera_config_signature(cameras: List[CameraConfig]) -> tuple:
    return tuple(sorted(_camera_signature(cam) for cam in cameras))


# ─────────────────────────────────────────────────────────────────────────────
# Frame Processing Callback  (called from stream threads)
# ─────────────────────────────────────────────────────────────────────────────

# Global state additions
violation_manager: Optional['ViolationManager'] = None

def on_frame(frame, cam: CameraConfig):
    """
    Core frame handler. Called by every RtspStreamClient thread.
    Enhanced with temporal deduplication via ViolationManager.
    """
    global _streams_paused

    # D-1 fix: snapshot the module-level singletons into locals at the top of
    # the function.  ``api_client``, ``stream_manager``, ``violation_manager``,
    # and ``main_loop`` are mutated only at startup (lifespan), but a hot-reload
    # could in theory replace them mid-frame.  Snapshotting closes the TOCTOU
    # gap: every subsequent reference in this invocation uses the same object
    # graph, so we can't end up POSTing through a closed api_client because
    # someone reassigned the global between two reads.
    _api = api_client
    _vm = violation_manager
    _loop = main_loop

    if _streams_paused:
        logger.warning("[%s] ⏸️  on_frame: streams are PAUSED - skipping", cam.camera_id)
        return

    # M12 fix: enforce the IsDetectionEnabled switch in the vision service
    # rather than relying purely on the backend to filter cameras out. If the
    # backend regresses we still respect the toggle.
    if not getattr(cam, "is_detection_enabled", True):
        logger.debug("[%s] is_detection_enabled=false; skipping frame.", cam.camera_id)
        return

    try:
        # ── DIAGNOSTIC: confirm on_frame is being called ──────────────────────
        num_rules = len(cam.violation_rules)
        # H17 fix: per-frame INFO log is a firehose (50 cams x 5 fps = 21M
        # lines/day). Drop to DEBUG; ops can opt in by lowering the level.
        logger.debug("[%s] 🔍 on_frame called | rules=%d | paused=%s",
                    cam.camera_id, num_rules, _streams_paused)

        if num_rules == 0:
            logger.debug("[%s] No active rules configured; skipping frame.", cam.camera_id)

            return

        # 1. Local AI Inference via Modular Engine
        # NOTE: Do NOT pre-resize 1080p or smaller frames — YOLO does its own
        # internal letterbox to imgsz, and manually shrinking 1920x1080 →
        # 640x480 before YOLO destroys ~75% of pixel information that the model
        # needs to recognize small features (masks, hairnets, gloves).
        # Empirically, the same frame produced no_glove score 0.13 after
        # pre-resize vs 0.57 at native resolution.
        #
        # P-1 fix: 4K (3840×2160) frames are 25 MB each — transferring them
        # to the GPU as-is is pure waste because YOLO will letterbox them down
        # to imgsz=640 anyway.  Pre-letterboxing 4K → 1080p preserves all the
        # fine detail YOLO can actually use while halving pixel transfer
        # bandwidth at 10×4K cameras (250 MB/s → 110 MB/s).  Aspect-preserving
        # so we don't distort objects.
        orig_h, orig_w = frame.shape[:2]
        if orig_w > 1920 or orig_h > 1080:
            scale = min(1920.0 / orig_w, 1080.0 / orig_h)
            new_w, new_h = int(orig_w * scale), int(orig_h * scale)
            frame = cv2.resize(frame, (new_w, new_h), interpolation=cv2.INTER_AREA)
            orig_h, orig_w = new_h, new_w
        target_size = (orig_w, orig_h)  # kept for downstream box scaling + evaluator frame_size
        rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        pil_image = Image.fromarray(rgb_frame)

        # Audit P4 #17: latency + throughput counters for /metrics.
        vision_metrics.frames_processed_total.labels(camera_id=cam.camera_id).inc()
        with vision_metrics.inference_latency_seconds.labels(camera_id=cam.camera_id).time():
            results = inference_engine.run_inference(pil_image, cam.violation_rules, camera_id=cam.camera_id)
        for _det in results:
            vision_metrics.detections_total.labels(
                camera_id=cam.camera_id,
                model_id=str(_det.get("model_id", "unknown")),
            ).inc()

        # Pre-tracker pass: inject `track_id` on raw detections BEFORE the
        # evaluator runs. Dwell rules need a stable per-subject identity to
        # accumulate dwell time; without this they fall back to a quantized
        # centroid bucket which is much coarser. ViolationManager.process_frame
        # below detects pre-existing track_ids and skips re-tracking so we
        # don't double-count missing frames.
        if _vm is not None:
            try:
                _vm.tag_tracks(cam.camera_id, results)
            except Exception:  # noqa: BLE001
                logger.exception("[%s] pre-tracker tag_tracks failed; dwell rules will use fallback id.", cam.camera_id)

        # 1.1 Data Collection (Active Learning) — fire-and-forget on a
        # dedicated side-effect pool so blocking disk I/O can't throttle
        # this camera's capture thread. Errors are logged via the future
        # callback; nothing downstream waits on the result.
        _side_effect_pool.submit(
            _safe_collect, pil_image, list(results), cam.camera_id, cam.tenant_id
        )
        
        # Determine actual violations using spatial logic rules.
        # frame_size is the native frame canvas; normalized polygon rules
        # resolve against the same pixel coords the model emitted.
        validated_violations = evaluate_violations(
            results, cam.violation_rules, frame_size=target_size, camera_id=cam.camera_id
        )

        # 2. State Management & Deduplication
        if _vm is None or _loop is None:
            logger.error("[%s] violation_manager / main_loop is None!", cam.camera_id)
            return

        future = asyncio.run_coroutine_threadsafe(
            _vm.process_frame(cam.camera_id, validated_violations, cam.violation_rules),
            _loop
        )
        # Bounded wait for the state decision. An unbounded .result() let a
        # stalled event loop / violation manager freeze this capture thread
        # forever (frames then pile up in the RTSP buffer until OOM). On
        # timeout we drop THIS frame and keep the stream alive.
        decision_timeout = float(getattr(config, "FRAME_DECISION_TIMEOUT_SECONDS", 5.0))
        try:
            actions = future.result(timeout=decision_timeout)
        except FutureTimeoutError:
            future.cancel()
            logger.warning(
                "[%s] process_frame state decision timed out after %.1fs — dropping frame",
                cam.camera_id, decision_timeout,
            )
            return

        if not actions:
            return

        # 3. Handle Actions (New Violation or Update Existing)
        new_actions = []
        update_actions = []

        # First, categorize and draw ALL bounding boxes on the frame so the snapshot is complete
        for action in actions:
            status = action["StateStatus"]
            det = action["Metadata"]
            track_id = action["TrackId"]
            
            if status == "New":
                new_actions.append(action)
            elif status == "Update":
                update_actions.append(action)

            # Draw on frame for visual feedback (snapshot will capture all boxes)
            # Detection boxes are already in the same coordinate space as `frame`
            # because we no longer pre-resize before inference.
            box = det["box"]
            xmin, ymin = int(box["xmin"]), int(box["ymin"])
            xmax, ymax = int(box["xmax"]), int(box["ymax"])
            
            color = (0, 0, 255) # Red for active violations
            cv2.rectangle(frame, (xmin, ymin), (xmax, ymax), color, 3)
            cv2.putText(frame, f"ID:{track_id} {det['label']} {det['score']:.2f}", (xmin, ymin - 10),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, color, 2)

        # 4. Take a SINGLE snapshot if there are any New violations.
        # The S3 key is deterministic, so we can compute the public URL up
        # front and hand the (blocking) put_object off to the side-effect
        # pool — the capture thread no longer stalls on S3 latency. boto3 is
        # configured with tight timeouts + 1 attempt (see client init above),
        # so a stuck upload can't back up the 4-worker pool for long. If the
        # upload ultimately fails the violation still posts; its FramePath
        # will 404, which matches the previous behaviour (empty FramePath).
        frame_url = ""
        if new_actions and not config.TESTING_MODE and s3_client and config.S3_BUCKET_NAME:
            annotated_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            pil_save = Image.fromarray(annotated_rgb)
            filename = f"violations/{cam.tenant_id}/{cam.camera_id}/{datetime.now(timezone.utc).strftime('%Y-%m-%d')}/{uuid.uuid4()}.jpg"
            buf = io.BytesIO()
            pil_save.save(buf, format="JPEG")
            snapshot_bytes = buf.getvalue()
            frame_url = f"https://{config.S3_BUCKET_NAME}.s3.{config.AWS_REGION}.amazonaws.com/{filename}"

            def _upload_snapshot(body=snapshot_bytes, key=filename, c_id=cam.camera_id):
                try:
                    s3_client.put_object(
                        Bucket=config.S3_BUCKET_NAME, Key=key,
                        Body=body, ContentType="image/jpeg",
                    )
                except Exception as e:  # noqa: BLE001
                    logger.warning("[%s] S3 snapshot upload failed (background): %s", c_id, e)

            _side_effect_pool.submit(_upload_snapshot)

        # 5. Dispatch API Calls
        #
        # P-2 phase 2 (true fire-and-forget): the capture thread used to block
        # for up to 3 s on identify_person before POSTing the violation, so a
        # slow reid service throttled the hot loop.  We now build & POST the
        # violation from inside the reid pool's done-callback thread.  The
        # capture thread merely *submits* reid and returns immediately; the
        # POST happens once the identity is known (or once reid times out and
        # fails open).  Net result: the capture thread is never blocked on
        # network I/O, regardless of reid latency.
        def _build_and_post(
            employee_id: Optional[str],
            is_unauthorized: bool,
            unknown_person_id: Optional[str] = None,
            *,
            action=None,
            det=None,
            cam_local=cam,
            frame_url_local=frame_url,
            track_id_local: int = 0,
            api=_api,
            loop=_loop,
        ):
            # Mutate a deep-copied det so we don't race with other callbacks
            # holding references to the same dict.  ViolationManager already
            # deep-copies before stashing, but the dict we received here is
            # the live one from ``new_actions``.
            det = copy.deepcopy(det)
            det["isUnauthorized"] = is_unauthorized
            det["employeeId"] = employee_id
            if unknown_person_id:
                det["unknownPersonId"] = unknown_person_id

            dedupe_key = _identity_dedupe_key(
                camera_db_id=cam_local.camera_db_id,
                sop_violation_type_id=action.get("SopViolationTypeId") if action else None,
                employee_id=employee_id,
                unknown_person_id=unknown_person_id,
            )
            if _identity_recently_posted(dedupe_key, time.time()):
                logger.info(
                    "[%s] Identity dedupe suppressed repeat violation (Track %d)",
                    cam_local.camera_id,
                    track_id_local,
                )
                return

            payload = {
                "TenantId": cam_local.tenant_id,
                "CameraId": cam_local.camera_db_id,
                "ModelIdentifier": action.get("ModelIdentifier"),
                "SopViolationTypeId": action.get("SopViolationTypeId"),
                "CorrelationId": str(uuid.uuid4()),
                "TrackId": track_id_local,
                "Timestamp": datetime.now(timezone.utc).isoformat(),
                "FramePath": frame_url_local,  # Shared URL for all new violations in this frame
                "Status": "Pending",
                "MetadataJson": json.dumps(det),
                # EmployeeExternalId is a string like "EMP-099" resolved to a Guid FK by the backend
                "EmployeeExternalId": employee_id,
            }
            try:
                future = asyncio.run_coroutine_threadsafe(api.post_violation(payload), loop)
                # M9 fix: count violations that survived all rule filters and
                # were queued for delivery. Distinct from api_post_total which
                # tracks HTTP outcomes; this measures rule-engine yield.
                try:
                    vision_metrics.violations_emitted_total.labels(
                        camera_id=cam_local.camera_id,
                        model_id=str(action.get("ModelIdentifier") or "unknown"),
                    ).inc()
                except Exception:
                    pass
            except RuntimeError:
                logger.exception("[%s] event loop closed; dropping violation for Track %d",
                                 cam_local.camera_id, track_id_local)
                return

            def _post_done(f, c_id=cam_local.camera_id, t_id=track_id_local):
                try:
                    f.result()
                except Exception:  # noqa: BLE001
                    # D-4 fix: logger.exception captures the full stack.
                    logger.exception("[%s] \u274c post_violation crashed silently (Track %d)", c_id, t_id)
            future.add_done_callback(_post_done)
            logger.info("[%s] \U0001f6a8 NEW Violation Event created for Track %d", cam_local.camera_id, track_id_local)

        for action in new_actions:
            det = action["Metadata"]
            track_id = action["TrackId"]

            if "person_box" in det:
                # Fire-and-forget reid.  When it completes (or fails), the
                # callback builds & POSTs the violation with the resolved
                # employee_id.  Capture thread never blocks here.
                identity_future = _reid_pool.submit(
                    identify_person, rgb_frame, det["person_box"], str(cam.tenant_id), cam.camera_id
                )

                def _on_reid_done(
                    fut,
                    action=action,
                    det=det,
                    track_id_local=track_id,
                    c_id=cam.camera_id,
                ):
                    try:
                        ident = fut.result() or {}
                    except Exception:  # noqa: BLE001
                        logger.exception("[%s] identify_person failed for Track %d", c_id, track_id_local)
                        ident = {}
                    _build_and_post(
                        ident.get("employeeId"),
                        ident.get("isUnauthorized", False),
                        ident.get("unknownPersonId"),
                        action=action,
                        det=det,
                        track_id_local=track_id_local,
                    )

                identity_future.add_done_callback(_on_reid_done)
            else:
                # No person box \u2014 skip reid entirely; POST immediately.
                _build_and_post(
                    None, False,
                    action=action,
                    det=det,
                    track_id_local=track_id,
                )

        for action in update_actions:
            track_id = action["TrackId"]
            timestamp = datetime.now(timezone.utc).isoformat()
            
            async def update_async(cid=cam.camera_db_id, tid=track_id, ts=timestamp, api=_api):
                active_v = await api.get_active_violation(cid, tid)
                if active_v and "id" in active_v:
                    await api.update_violation(active_v["id"], ts)
                    logger.debug("[%s] Updated last_seen for Track %d (Event: %s)", cam.camera_id, tid, active_v["id"])
                else:
                    pass

            future = asyncio.run_coroutine_threadsafe(update_async(), _loop)
            def _update_done(f, c_id=cam.camera_id):
                try:
                    f.result()
                except Exception:  # noqa: BLE001
                    logger.exception("[%s] ❌ update_violation crashed silently", c_id)
            future.add_done_callback(_update_done)

    except Exception:  # noqa: BLE001
        # D-4 fix: full traceback so production debugging can pinpoint the
        # failing line in inference / evaluator / state-machine code.
        logger.exception("[%s] on_frame error", cam.camera_id)



# ─────────────────────────────────────────────────────────────────────────────
# Background: Camera Poll Loop
# ─────────────────────────────────────────────────────────────────────────────

# ─────────────────────────────────────────────────────────────────────────────
# Removed: Camera Poll Loop. Replaced with Webhook POST /streams/reload
# ─────────────────────────────────────────────────────────────────────────────


# ─────────────────────────────────────────────────────────────────────────────
# Config-change heartbeat
# ─────────────────────────────────────────────────────────────────────────────

async def _config_poll_loop() -> None:
    """
    Background task that wakes every CONFIG_POLL_INTERVAL_SECONDS (default 1h)
    and checks whether the camera assignments for this device have changed in
    the Violation API.  If they have, it calls stream_manager.reconcile() so
    cameras added/removed/reassigned in the dashboard take effect automatically
    without needing a service restart.

    Log format:
      🔄 Config poll — no changes (3 cameras)
      🔄 Config poll — added=['CAM-004'] removed=['CAM-001'] — reconciling
    """
    global _last_config_signature
    interval = config.CONFIG_POLL_INTERVAL_SECONDS
    logger.info("🔄 Config-poll heartbeat scheduled (interval: %ds)", interval)
    while True:
        await asyncio.sleep(interval)
        try:
            if api_client is None or stream_manager is None:
                continue

            fresh = await api_client.fetch_active_cameras(device_id=edge_device_id)
            # V5 fix: None = fetch FAILED (network/5xx/auth). Do NOT reconcile —
            # reconciling against a failed fetch used to tear down every
            # healthy stream on a transient API blip.
            if fresh is None:
                logger.warning(
                    "🔄 Config poll — camera fetch failed; keeping existing "
                    "streams untouched (will retry in %ds)", interval,
                )
                continue
            fresh = _apply_config(fresh)

            fresh_signature = _camera_config_signature(fresh)
            if _last_config_signature != fresh_signature:
                logger.info("🔄 Config poll — change detected, reconciling %d camera(s)", len(fresh))
            else:
                logger.info("🔄 Config poll — no changes (%d cameras)", len(fresh))

            # V3 fix: reconcile UNCONDITIONALLY on every successful fetch, not
            # only on signature change. reconcile() is idempotent for healthy
            # streams and restarts any stream sitting in 'stopped'/'error'
            # state — previously a dead stream stayed dead forever unless the
            # config happened to change.
            await stream_manager.reconcile(fresh)
            _last_config_signature = fresh_signature
            # H13 fix: prune dwell entries that belong to cameras no
            # longer in the active set so the module-level store can
            # shrink. Best-effort — a failure here must not abort the
            # poll loop.
            try:
                from rules import dwell as _dwell
                _dwell.clear_unknown_cameras({c.camera_id for c in fresh})
            except Exception:  # noqa: BLE001
                logger.debug("dwell.clear_unknown_cameras failed (non-fatal)", exc_info=True)
        except asyncio.CancelledError:
            logger.info("🔄 Config-poll loop cancelled")
            raise
        except Exception:
            logger.exception(
                "🔄 Config-poll error (will retry in %ds)", interval
            )


# ─────────────────────────────────────────────────────────────────────────────
# FastAPI Lifespan
# ─────────────────────────────────────────────────────────────────────────────

@asynccontextmanager
async def lifespan(app: FastAPI):
    global api_client, stream_manager, violation_manager, main_loop, edge_device_id, _last_config_signature
    main_loop = asyncio.get_running_loop()

    config.log_config(logger)

    api_client = ViolationApiClient(
        base_url=config.VIOLATION_API_BASE_URL,
        api_key=config.INTERNAL_API_KEY,
        dlq_persist_path=config.DLQ_PERSIST_PATH,
    )
    # Start background DLQ drain (audit P3 #11) so violations queued during a
    # transient API outage are eventually delivered.
    api_client.start_background_workers()

    # Register this edge device with the Violation API. The returned UUID is
    # used to scope all subsequent /cameras/internal/active calls. If no
    # DEVICE_TENANT_ID is configured we fall back to the legacy "all cameras"
    # mode (good for single-device dev setups).
    device_identifier = get_device_identifier()
    edge_device_id = await register_device(
        api_client,
        device_identifier,
        tenant_id=config.DEVICE_TENANT_ID,
        display_name=config.DEVICE_DISPLAY_NAME,
    )
    if edge_device_id:
        logger.info("⚙️  Vision Service scoped to edge device %s", edge_device_id)
    else:
        logger.info("⚙️  Vision Service running in legacy mode — no device scoping")
    # entry_hysteresis=3 -> ~3s of continuous detection at targetFps=1 before
    # a New event fires (was 5 = 5s, too long for typical kitchen pass-throughs).
    # exit_buffer kept at 10 to avoid flapping when subject momentarily occludes.
    violation_manager = ViolationManager(entry_hysteresis=3, exit_buffer=10)

    # C-3 fix: wire the stream client's reconnect callback to the violation
    # manager.  Whenever a camera reconnects after a disconnect, drop tracker
    # and state so we don't carry stale (track_id, sop_id) entries that lock
    # up the LRU cap or keep Cooldown alive for minutes.
    def _on_camera_reconnect(camera_id: str) -> None:
        if violation_manager is not None:
            violation_manager.reset_camera(camera_id)

    stream_manager = CameraStreamManager(
        frame_callback=on_frame,
        max_workers=config.MAX_STREAM_WORKERS,
        on_reconnect=_on_camera_reconnect,
    )

    cameras = await api_client.fetch_active_cameras(device_id=edge_device_id)
    if cameras is None:
        # V5 fix: startup fetch failed — start with no streams but leave the
        # signature unset so the first successful config poll reconciles.
        logger.warning(
            "⚠️  Startup camera fetch failed — starting with 0 streams; "
            "config poll will keep retrying every %ds.",
            config.CONFIG_POLL_INTERVAL_SECONDS,
        )
        cameras = []
        _last_config_signature = None
    else:
        cameras = _apply_config(cameras)
        # Load cameras into memory on boot so manual endpoints work immediately
        await stream_manager.reconcile(cameras)
        _last_config_signature = _camera_config_signature(cameras)

    if stream_manager.active_count > 0:
        logger.info("▶️  Vision Engine started with %d active streams.", stream_manager.active_count)
    else:
        logger.info("⏸️  Vision Engine started in IDLE mode. No active cameras returned by API.")

    # Hourly config-change heartbeat: detects cameras added/removed/reassigned
    # in the dashboard and reconciles the stream manager automatically.
    # Interval is CONFIG_POLL_INTERVAL_SECONDS (default 3600s / 1 hour).
    poll_task = asyncio.create_task(_config_poll_loop(), name="config-poll")

    yield  # ← Service is running

    poll_task.cancel()
    try:
        await poll_task
    except asyncio.CancelledError:
        pass

    logger.info("🛑 Shutting down...")
    await stream_manager.stop_all()
    if api_client is not None:
        await api_client.aclose()
    # M24 fix: drain both fire-and-forget thread pools so in-flight collector
    # writes and re-id lookups don't leave truncated JSON / partial HTTP
    # connections behind.
    _side_effect_pool.shutdown(wait=True, cancel_futures=False)
    _reid_pool.shutdown(wait=False, cancel_futures=True)
    logger.info("👋 Shutdown complete")


# ─────────────────────────────────────────────────────────────────────────────
# FastAPI App
# ─────────────────────────────────────────────────────────────────────────────
app = FastAPI(
    title="Alpha Surveillance — Vision Inference Service",
    description=(
        "AI-powered violation detection service. "
        "RTSP stream engine (with testing mode) + manual frame upload."
    ),
    version="2.1.0",
    lifespan=lifespan,
)


# Audit P4 #17: Prometheus scrape endpoint. Exposes the counters/histograms
# declared in metrics.py in text exposition format. No auth — same trust
# boundary as /health; gate behind the internal LB if needed.
@app.get("/metrics", tags=["Health"])
def metrics_endpoint():
    # Refresh gauges that have to be sampled on-demand.
    if api_client is not None:
        try:
            vision_metrics.api_dlq_size.set(len(getattr(api_client, "_dlq", [])))
        except Exception:  # noqa: BLE001
            pass
    body, content_type = vision_metrics.render_text()
    return Response(content=body, media_type=content_type)

# ─────────────────────────────────────────────────────────────────────────────
# RTSP Management Endpoints
# ─────────────────────────────────────────────────────────────────────────────

@app.get("/streams/status", tags=["RTSP Engine"])
async def get_stream_status():
    """Live status of all camera streams + pause state + testing mode flag."""
    if stream_manager is None:
        return JSONResponse(status_code=503, content={"error": "Not initialised"})

    states = stream_manager.get_all_states()
    return {
        "testing_mode":  config.TESTING_MODE,
        "streams_paused": _streams_paused,
        "total":       len(states),
        "running":     sum(1 for s in states if s["status"] == "running"),
        "reconnecting": sum(1 for s in states if s["status"] == "reconnecting"),
        "error":       sum(1 for s in states if s["status"] == "error"),
        "streams":     states,
    }


@app.post("/streams/pause", tags=["RTSP Engine"], dependencies=[Depends(require_internal_api_key)])
async def pause_streams():
    """
    Pause frame processing on ALL streams.
    OpenCV threads keep running (instant resume), but on_frame() becomes a no-op.
    No AI inference → no S3/SQS calls → zero cost while paused.
    """
    global _streams_paused
    _streams_paused = True
    logger.info("⏸️  All stream processing PAUSED")
    return {"paused": True, "message": "All streams paused. Call /streams/resume to restart processing."}


@app.post("/streams/resume", tags=["RTSP Engine"], dependencies=[Depends(require_internal_api_key)])
async def resume_streams():
    """Resume frame processing after a pause."""
    global _streams_paused
    _streams_paused = False
    logger.info("▶️  All stream processing RESUMED")
    return {"paused": False, "message": "All streams resumed."}


@app.post("/streams/reload", tags=["RTSP Engine"], dependencies=[Depends(require_internal_api_key)])
async def reload_streams():
    """Hot-reload cameras from the Violation API: adds new, removes deleted, keeps running streams."""
    if stream_manager is None or api_client is None:
        return JSONResponse(status_code=503, content={"error": "Not initialised"})
    cameras = await api_client.fetch_active_cameras(device_id=edge_device_id)
    if cameras is None:
        # V5 fix: fetch failed — never reconcile to zero on an API blip.
        logger.warning("/streams/reload: camera fetch failed — existing streams left untouched")
        return JSONResponse(
            status_code=503,
            content={
                "error": "Camera fetch from Violation API failed; existing streams were NOT modified.",
                "active_streams": stream_manager.active_count,
            },
        )
    cameras = _apply_config(cameras)
    await stream_manager.reconcile(cameras)
    return {
        "message":        f"Reloaded {len(cameras)} cameras from API",
        "active_streams": stream_manager.active_count,
        "streams_paused": _streams_paused,
    }


@app.get("/streams/{camera_id}", tags=["RTSP Engine"])
async def get_single_stream_status(camera_id: str):
    """Status of a single camera stream by its camera_id slug."""
    if stream_manager is None:
        return JSONResponse(status_code=503, content={"error": "Not initialised"})
    state = stream_manager.get_camera_state(camera_id)
    if state is None:
        return JSONResponse(status_code=404, content={"error": f"Camera '{camera_id}' not found"})
    return state


# ─────────────────────────────────────────────────────────────────────────────
# RTSP URL Probe Endpoint  (does NOT add to live stream manager)
# ─────────────────────────────────────────────────────────────────────────────

from pydantic import BaseModel

class RtspProbeRequest(BaseModel):
    url: str
    timeout_seconds: float = 8.0   # how long to wait for the first frame


def _probe_rtsp_blocking(url: str, timeout: float) -> dict:
    """
    Runs in a thread-pool thread (blocking OpenCV calls are safe here).
    Tries to open the RTSP stream and grab at least one frame within `timeout` seconds.
    Returns a diagnostics dict.
    """
    import time as _time
    start = _time.monotonic()

    cap = cv2.VideoCapture(url, cv2.CAP_FFMPEG)
    opened = cap.isOpened()

    result = {
        "url":           url,
        "reachable":     False,
        "got_frame":     False,
        "backend":       "FFMPEG/OpenCV",
        "elapsed_ms":    0,
        "frame_width":   None,
        "frame_height":  None,
        "fps_reported":  None,
        "error":         None,
    }

    if not opened:
        cap.release()
        result["error"] = "cv2.VideoCapture failed to open — stream unreachable or URL invalid"
        result["elapsed_ms"] = int((_time.monotonic() - start) * 1000)
        return result

    result["reachable"]    = True
    result["fps_reported"] = cap.get(cv2.CAP_PROP_FPS) or None

    # Try to grab one frame within the timeout
    deadline = start + timeout
    while _time.monotonic() < deadline:
        ret, frame = cap.read()
        if ret and frame is not None:
            h, w = frame.shape[:2]
            result["got_frame"]    = True
            result["frame_width"]  = w
            result["frame_height"] = h
            break
        _time.sleep(0.1)

    cap.release()
    result["elapsed_ms"] = int((_time.monotonic() - start) * 1000)

    if not result["got_frame"]:
        result["error"] = (
            f"Stream opened but no frame received within {timeout}s — "
            "may be authenticating, buffering, or the feed is paused"
        )

    return result


@app.post("/streams/test", tags=["RTSP Engine"], dependencies=[Depends(require_internal_api_key)])
async def test_rtsp_url(body: RtspProbeRequest):
    """
    Probe ANY RTSP URL without adding it to the live stream manager.
    Returns whether the URL is reachable and producing frames.

    - `reachable`: OpenCV could open the connection
    - `got_frame`: at least one video frame was decoded within `timeout_seconds`
    - `frame_width/height`: resolution of the first frame received
    - `fps_reported`: FPS advertised by the stream header
    - `elapsed_ms`: how long the probe took

    Useful for verifying OctoStream / public RTSP test URLs before adding cameras.
    """
    # C5 fix: SSRF guard — reject non-rtsp schemes (file://, http://) and
    # link-local / metadata hosts before we touch cv2.VideoCapture.
    err = _validate_safe_rtsp_url(body.url)
    if err is not None:
        return JSONResponse(status_code=400, content={"error": err, "url": body.url})

    loop = asyncio.get_event_loop()
    result = await loop.run_in_executor(
        None,  # default executor (ThreadPoolExecutor)
        _probe_rtsp_blocking,
        body.url,
        body.timeout_seconds,
    )

    status = 200 if result["got_frame"] else 422
    return JSONResponse(status_code=status, content=result)




# ─────────────────────────────────────────────────────────────────────────────
# Original: Manual Frame Upload (kept for testing)
# ─────────────────────────────────────────────────────────────────────────────

@app.get("/", response_class=HTMLResponse, tags=["Testing"])
async def read_root():
    mode_banner = (
        '<div style="background:#b45309;color:#fff;padding:10px 20px;border-radius:6px;margin-bottom:16px;">'
        '⚠️ TESTING MODE — AWS calls disabled (S3 / SQS skipped). Safe to run.</div>'
        if config.TESTING_MODE else
        '<div style="background:#166534;color:#fff;padding:10px 20px;border-radius:6px;margin-bottom:16px;">'
        '🚀 PRODUCTION MODE — AWS (S3 / SQS) enabled.</div>'
    )
    return f"""
    <!DOCTYPE html>
    <html>
    <head>
        <title>Vision Inference Service</title>
        <style>
            body {{ font-family: sans-serif; max-width: 900px; margin: 0 auto; padding: 20px; background: #0d1117; color: #c9d1d9; }}
            h1 {{ color: #58a6ff; }} h3 {{ color: #8b949e; }}
            .container {{ border: 1px solid #30363d; padding: 20px; border-radius: 8px; background: #161b22; margin-bottom: 16px; }}
            #result {{ white-space: pre-wrap; background: #0d1117; padding: 10px; border-radius: 4px; font-family: monospace; font-size: 12px; margin-top:12px; }}
            img {{ max-width: 100%; margin-top: 12px; border: 1px solid #30363d; border-radius: 4px; }}
            .form-group {{ margin-bottom: 12px; }}
            label {{ display: block; margin-bottom: 4px; font-weight: bold; color: #8b949e; font-size: 13px; }}
            input[type="text"] {{ width: 100%; padding: 8px; box-sizing: border-box; background: #0d1117; border: 1px solid #30363d; color: #c9d1d9; border-radius: 4px; }}
            .btn {{ color: white; border: none; padding: 8px 18px; border-radius: 6px; cursor: pointer; font-size: 14px; margin-right:8px; }}
            .btn-green {{ background:#238636; }} .btn-green:hover {{ background:#2ea043; }}
            .btn-yellow {{ background:#9a3412; }} .btn-yellow:hover {{ background:#b45309; }}
            .btn-blue {{ background:#1f6feb; }} .btn-blue:hover {{ background:#388bfd; }}
            a {{ color: #58a6ff; }}
        </style>
    </head>
    <body>
        <h1>🎯 Alpha Surveillance — Vision Inference Service</h1>
        {mode_banner}
        <div class="container">
            <h3>📡 RTSP Stream Engine</h3>
            <button class="btn btn-blue"   onclick="fetch('/streams/status').then(r=>r.json()).then(d=>document.getElementById('eng-result').textContent=JSON.stringify(d,null,2))">Status</button>
            <button class="btn btn-yellow" onclick="fetch('/streams/pause',{{method:'POST'}}).then(r=>r.json()).then(d=>document.getElementById('eng-result').textContent=JSON.stringify(d,null,2))">⏸ Pause All</button>
            <button class="btn btn-green"  onclick="fetch('/streams/resume',{{method:'POST'}}).then(r=>r.json()).then(d=>document.getElementById('eng-result').textContent=JSON.stringify(d,null,2))">▶ Resume All</button>
            <button class="btn btn-blue"   onclick="fetch('/streams/reload',{{method:'POST'}}).then(r=>r.json()).then(d=>document.getElementById('eng-result').textContent=JSON.stringify(d,null,2))">🔄 Reload Cameras</button>
            <div id="eng-result" style="color:#58a6ff;margin-top:10px;font-family:monospace;font-size:12px;">Click a button above.</div>
        </div>
        <div class="container">
            <h3>🔌 RTSP URL Probe — Test Any Link</h3>
            <p style="font-size:13px;color:#8b949e;margin-top:0">Checks if an RTSP URL is reachable and producing frames. Does NOT add it as a live camera.</p>
            <div class="form-group">
                <label>RTSP URL:</label>
                <input type="text" id="probeUrl" placeholder="rtsp://username:password@host:port/path" style="font-family:monospace">
            </div>
            <div class="form-group" style="display:flex;align-items:center;gap:12px;">
                <label style="margin:0;white-space:nowrap">Timeout:</label>
                <input type="range" id="probeTimeout" min="3" max="20" value="8" style="flex:1">
                <span id="probeTimeoutLabel" style="font-family:monospace;color:#58a6ff;min-width:30px">8s</span>
            </div>
            <button class="btn btn-blue" onclick="probeRtsp()">🔍 Test URL</button>
            <div id="probe-result" style="margin-top:12px;font-family:monospace;font-size:12px;white-space:pre-wrap;opacity:0.8">No probe run yet.</div>
        </div>
        <div class="container">
            <h3>🧪 Manual Frame Upload</h3>
            <div class="form-group"><label>Camera ID:</label><input type="text" id="cameraId" value="CAM-002"></div>
            <div class="form-group"><label>Tenant ID:</label><input type="text" id="tenantId" value="{config.DEVICE_TENANT_ID or ''}"></div>
            <input type="file" id="fileInput" accept="image/*"><br><br>
            <button class="btn btn-green" onclick="uploadImage()">Analyze Frame</button>
            <div id="preview"></div>
            <div id="result">No result yet.</div>
        </div>

        <script>
            // Sync timeout slider label
            document.getElementById('probeTimeout').addEventListener('input', function() {{
                document.getElementById('probeTimeoutLabel').textContent = this.value + 's';
            }});

            async function probeRtsp() {{
                const url     = document.getElementById('probeUrl').value.trim();
                const timeout = parseFloat(document.getElementById('probeTimeout').value);
                const div     = document.getElementById('probe-result');
                if (!url) {{ alert('Paste an RTSP URL first.'); return; }}
                div.style.color = '#8b949e';
                div.textContent = `⏳ Probing (timeout: ${{timeout}}s) — this may take a moment...`;
                try {{
                    const r    = await fetch('/streams/test', {{
                        method: 'POST',
                        headers: {{'Content-Type': 'application/json'}},
                        body: JSON.stringify({{ url, timeout_seconds: timeout }}),
                    }});
                    const data = await r.json();
                    if (data.got_frame) {{
                        div.style.color = '#3fb950';
                        div.textContent = `✅ WORKING — ${{data.frame_width}}×${{data.frame_height}} px  |  FPS header: ${{data.fps_reported ?? 'n/a'}}  |  Took: ${{data.elapsed_ms}}ms\n\n` + JSON.stringify(data, null, 2);
                    }} else if (data.reachable) {{
                        div.style.color = '#f0883e';
                        div.textContent = `⚠️ REACHABLE BUT NO FRAME\n${{data.error}}\n\n` + JSON.stringify(data, null, 2);
                    }} else {{
                        div.style.color = '#f85149';
                        div.textContent = `❌ UNREACHABLE\n${{data.error}}\n\n` + JSON.stringify(data, null, 2);
                    }}
                }} catch(e) {{
                    div.style.color = '#f85149';
                    div.textContent = 'Fetch error: ' + e.message;
                }}
            }}

            async function uploadImage() {{
                const fInput = document.getElementById('fileInput');
                const cameraId = document.getElementById('cameraId').value;
                const tenantId = document.getElementById('tenantId').value;
                const resultDiv = document.getElementById('result');
                if (!fInput.files.length) {{ alert("Select a file first."); return; }}
                const file = fInput.files[0];
                const reader = new FileReader();
                reader.onload = e => document.getElementById('preview').innerHTML = `<img src="${{e.target.result}}" alt="Preview">`;
                reader.readAsDataURL(file);
                const fd = new FormData();
                fd.append("file", file); fd.append("camera_id", cameraId); fd.append("tenant_id", tenantId);
                resultDiv.textContent = "Analyzing...";
                try {{
                    const r = await fetch("/analyze", {{method:"POST", body:fd}});
                    resultDiv.textContent = JSON.stringify(await r.json(), null, 2);
                }} catch(e) {{ resultDiv.textContent = "Error: " + e.message; }}
            }}
        </script>

    </body>
    </html>
    """


def _is_video_upload(upload: UploadFile) -> bool:
    name = (upload.filename or "").lower()
    ctype = (upload.content_type or "").lower()
    return ctype.startswith("video/") or name.endswith((".mp4", ".mov", ".avi", ".mkv", ".dav"))


async def _resolve_active_camera(camera_id: str) -> Optional[CameraConfig]:
    if not api_client:
        return None
    cameras = await api_client.fetch_active_cameras(device_id=edge_device_id)
    if cameras is None:
        # V5 fix: fetch failed — caller (/analyze) reports "camera not found"
        # rather than crashing on a None iteration.
        logger.warning("_resolve_active_camera: camera fetch failed")
        return None
    cameras = _apply_config(cameras)
    return next((c for c in cameras if c.camera_id == camera_id), None)


async def _process_analyze_frame(
    frame_bgr: np.ndarray,
    cam: CameraConfig,
    tenant_id: str,
    *,
    frame_index: int,
    include_side_effects: bool,
) -> dict:
    # H9 fix: production `on_frame` aspect-preserves 4K → 1080p so detector
    # output is consistent with what RTSP cameras feed in. /analyze
    # previously skipped this step, producing different detections for the
    # same content. Mirror the exact transform here.
    orig_h, orig_w = frame_bgr.shape[:2]
    if orig_w > 1920 or orig_h > 1080:
        scale = min(1920.0 / orig_w, 1080.0 / orig_h)
        new_w, new_h = int(orig_w * scale), int(orig_h * scale)
        frame_bgr = cv2.resize(frame_bgr, (new_w, new_h), interpolation=cv2.INTER_AREA)
    frame_h, frame_w = frame_bgr.shape[:2]
    rgb = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2RGB)
    pil_image = Image.fromarray(rgb)

    # Mirror production path: inference -> pre-tracker -> evaluator -> state machine
    detections = inference_engine.run_inference(pil_image, cam.violation_rules, camera_id=cam.camera_id)
    if violation_manager is not None:
        try:
            violation_manager.tag_tracks(cam.camera_id, detections)
        except Exception:
            logger.exception("[%s] analyze: tag_tracks failed", cam.camera_id)

    validated = evaluate_violations(
        detections,
        cam.violation_rules,
        frame_size=(frame_w, frame_h),
        camera_id=cam.camera_id,
    )

    if violation_manager is not None:
        actions = await violation_manager.process_frame(cam.camera_id, validated, cam.violation_rules)
    else:
        actions = []

    if include_side_effects:
        data_collector.collect_inference_event(pil_image, detections, cam.camera_id, tenant_id)

    new_actions = [a for a in actions if a.get("StateStatus") == "New"]
    update_actions = [a for a in actions if a.get("StateStatus") == "Update"]

    frame_url = ""
    posted_new = 0
    posted_updates = 0

    if include_side_effects and new_actions and not config.TESTING_MODE and s3_client and config.S3_BUCKET_NAME:
        annotated = frame_bgr.copy()
        for action in actions:
            det = action.get("Metadata", {})
            box = det.get("box") or {}
            try:
                xmin, ymin = int(box["xmin"]), int(box["ymin"])
                xmax, ymax = int(box["xmax"]), int(box["ymax"])
            except Exception:
                continue
            cv2.rectangle(annotated, (xmin, ymin), (xmax, ymax), (0, 0, 255), 3)

        filename = (
            f"violations/{tenant_id}/{cam.camera_id}/"
            f"{datetime.now(timezone.utc).strftime('%Y-%m-%d')}/{uuid.uuid4()}.jpg"
        )
        buf = io.BytesIO()
        Image.fromarray(cv2.cvtColor(annotated, cv2.COLOR_BGR2RGB)).save(buf, format="JPEG")
        buf.seek(0)
        s3_client.put_object(Bucket=config.S3_BUCKET_NAME, Key=filename, Body=buf, ContentType="image/jpeg")
        frame_url = f"https://{config.S3_BUCKET_NAME}.s3.{config.AWS_REGION}.amazonaws.com/{filename}"

    if include_side_effects and api_client and not config.TESTING_MODE:
        for action in new_actions:
            det = copy.deepcopy(action.get("Metadata", {}))
            employee_id: Optional[str] = None
            is_unauthorized = False
            if "person_box" in det:
                try:
                    ident = await asyncio.get_running_loop().run_in_executor(
                        _reid_pool,
                        identify_person,
                        rgb,
                        det["person_box"],
                        str(cam.tenant_id),
                    )
                    employee_id = (ident or {}).get("employeeId")
                    is_unauthorized = bool((ident or {}).get("isUnauthorized", False))
                except Exception as reid_err:
                    logger.warning("[%s] analyze re-ID failed: %s", cam.camera_id, reid_err)

            det["isUnauthorized"] = is_unauthorized
            det["employeeId"] = employee_id
            payload = {
                "TenantId": cam.tenant_id,
                "CameraId": cam.camera_db_id,
                "ModelIdentifier": action.get("ModelIdentifier"),
                "SopViolationTypeId": action.get("SopViolationTypeId"),
                "CorrelationId": str(uuid.uuid4()),
                "TrackId": action.get("TrackId", 0),
                "Timestamp": datetime.now(timezone.utc).isoformat(),
                "FramePath": frame_url,
                "Status": "Pending",
                "MetadataJson": json.dumps(det),
                "EmployeeExternalId": employee_id,
            }
            await api_client.post_violation(payload)
            posted_new += 1

        for action in update_actions:
            active_v = await api_client.get_active_violation(cam.camera_db_id, action.get("TrackId", 0))
            if active_v and "id" in active_v:
                await api_client.update_violation(active_v["id"], datetime.now(timezone.utc).isoformat())
                posted_updates += 1

    return {
        "frame_index": frame_index,
        "detections": detections,
        "validated_violations": validated,
        "actions": actions,
        "new_actions": len(new_actions),
        "update_actions": len(update_actions),
        "posted_new": posted_new,
        "posted_updates": posted_updates,
        "frame_url": frame_url,
    }


@app.post("/analyze", tags=["Production-Check"], dependencies=[Depends(require_internal_api_key)])
async def analyze(
    camera_id: str = Form(...),
    tenant_id: str = Form(...),
    file: UploadFile = File(...),
    include_side_effects: bool = Form(True),
    frame_stride: int = Form(1),
    max_frames: int = Form(300),
    simulate_realtime: bool = Form(False),
):
    """
    Production-parity analysis endpoint.

    Supports:
      - Images (single frame)
      - Videos (.mp4/.dav/.mov/.avi/.mkv) decoded into frames

    For every processed frame it runs the same core pipeline stages as RTSP:
      inference -> tracker tagging -> spatial evaluator (incl. dwell rules)
      -> ViolationManager state machine (new/update) -> optional post/update + re-id.
    """
    try:
        cam = await _resolve_active_camera(camera_id)
        if not cam:
            return JSONResponse(
                status_code=400,
                content={"error": f"Camera '{camera_id}' not found in active list. Cannot load Trigger Labels."},
            )

        # M17 fix: ‘/analyze’ used to share ViolationManager + dwell state
        # with the live RTSP camera, so an offline upload could mark the
        # live camera's tracks as cooling-down and silently suppress real
        # alerts. Run /analyze against a synthetic camera_id that's unique
        # per request, then clean it up in the outer ``finally``.
        import copy as _copy
        import uuid as _uuid
        analyze_suffix = _uuid.uuid4().hex[:8]
        analyze_camera_id = f"analyze:{cam.camera_id}:{analyze_suffix}"
        original_camera_id = cam.camera_id
        cam = _copy.copy(cam)
        cam.camera_id = analyze_camera_id

        raw = await file.read()
        if not raw:
            return JSONResponse(status_code=400, content={"error": "Uploaded file is empty."})

        stride = max(1, int(frame_stride))
        limit = max(1, int(max_frames))

        if not _is_video_upload(file):
            pil = Image.open(io.BytesIO(raw)).convert("RGB")
            frame_bgr = cv2.cvtColor(np.array(pil), cv2.COLOR_RGB2BGR)
            out = await _process_analyze_frame(
                frame_bgr,
                cam,
                tenant_id,
                frame_index=0,
                include_side_effects=include_side_effects,
            )
            return {
                "mode": "image",
                "testing_mode": config.TESTING_MODE,
                "filename": file.filename,
                "camera_id": cam.camera_id,
                "tenant_id": tenant_id,
                "violation_detected": len(out["actions"]) > 0,
                "detections": out["detections"],
                "violations": out["validated_violations"],
                "actions": out["actions"],
                "posted_new": out["posted_new"],
                "posted_updates": out["posted_updates"],
                "frame_url": out["frame_url"] or "(not uploaded — testing mode / no new violations)",
            }

        suffix = os.path.splitext(file.filename or "video.mp4")[1] or ".mp4"
        with tempfile.NamedTemporaryFile(suffix=suffix, delete=False) as tmp:
            tmp.write(raw)
            tmp_path = tmp.name

        cap = None
        try:
            cap = cv2.VideoCapture(tmp_path)
            if not cap.isOpened():
                return JSONResponse(status_code=400, content={"error": "Failed to decode video. Unsupported codec/container."})

            fps = cap.get(cv2.CAP_PROP_FPS) or 0.0
            frame_idx = -1
            processed = 0
            violations_total = 0
            posted_new_total = 0
            posted_updates_total = 0
            frame_summaries = []

            while processed < limit:
                ok, frame = cap.read()
                if not ok:
                    break
                frame_idx += 1
                if frame_idx % stride != 0:
                    continue

                out = await _process_analyze_frame(
                    frame,
                    cam,
                    tenant_id,
                    frame_index=frame_idx,
                    include_side_effects=include_side_effects,
                )

                processed += 1
                violations_total += len(out["actions"])
                posted_new_total += out["posted_new"]
                posted_updates_total += out["posted_updates"]

                frame_summaries.append(
                    {
                        "frame_index": frame_idx,
                        "detections": len(out["detections"]),
                        "validated_violations": len(out["validated_violations"]),
                        "new_actions": out["new_actions"],
                        "update_actions": out["update_actions"],
                    }
                )

                if simulate_realtime and fps > 0:
                    await asyncio.sleep(1.0 / fps)

            return {
                "mode": "video",
                "testing_mode": config.TESTING_MODE,
                "filename": file.filename,
                "camera_id": cam.camera_id,
                "tenant_id": tenant_id,
                "frames_processed": processed,
                "frame_stride": stride,
                "max_frames": limit,
                "source_fps": fps,
                "violation_actions_total": violations_total,
                "posted_new_total": posted_new_total,
                "posted_updates_total": posted_updates_total,
                "frame_summaries": frame_summaries,
                "note": "Dwell/re-id logic is production-parity. For realistic dwell timing, use simulate_realtime=true or RTSP mode.",
            }
        finally:
            # Leak fix: release the VideoCapture on EVERY exit path — an
            # exception mid-decode used to leak the FFmpeg demuxer + fd.
            if cap is not None:
                try:
                    cap.release()
                except Exception:  # noqa: BLE001
                    pass
            try:
                os.remove(tmp_path)
            except OSError:
                pass

    except Exception as e:
        logger.exception("/analyze failed")
        return JSONResponse(status_code=500, content={"error": str(e)})
    finally:
        # M17 fix: drop synthetic per-request state so /analyze can't leak
        # memory under heavy use. Both calls are idempotent.
        try:
            if violation_manager is not None and 'analyze_camera_id' in locals():
                violation_manager.reset_camera(analyze_camera_id)
        except Exception:  # noqa: BLE001
            logger.debug("analyze: cleanup reset_camera failed", exc_info=True)




# ─────────────────────────────────────────────────────────────────────────────
# Health
# ─────────────────────────────────────────────────────────────────────────────

# ─────────────────────────────────────────────────────────────────────────────
# Data Feedback Endpoint
# ─────────────────────────────────────────────────────────────────────────────

class FeedbackRequest(BaseModel):
    event_id: str
    is_correct: bool
    corrected_label: Optional[str] = None

@app.post("/feedback", tags=["Active Learning"], dependencies=[Depends(require_internal_api_key)])
async def project_feedback(body: FeedbackRequest):
    """
    Submit user feedback for a specific data collection event.
    Updates metadata to improve future training cycles.
    """
    data_collector.handle_user_feedback(
        body.event_id, 
        body.is_correct, 
        body.corrected_label
    )
    return {"status": "success", "message": f"Feedback recorded for {body.event_id}"}


@app.get("/health", tags=["Health"])
async def health():
    active = stream_manager.active_count if stream_manager else 0
    return {
        "status":        "ok",
        "testing_mode":  config.TESTING_MODE,
        "streams_paused": _streams_paused,
        "active_streams": active,
    }


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=config.PORT)
