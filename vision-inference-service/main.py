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
import uuid
import time
import logging
import asyncio
import cv2
import threading
import numpy as np
from datetime import datetime
from typing import Optional, List
from contextlib import asynccontextmanager

import boto3
from fastapi import FastAPI, UploadFile, File, Form
from fastapi.responses import HTMLResponse, JSONResponse
from transformers import pipeline
from PIL import Image, ImageDraw

import config  # central config file (reads .env + environment)
from rtsp import CameraStreamManager, ViolationApiClient, CameraConfig
from rtsp.violation_manager import ViolationManager

# ─────────────────────────────────────────────────────────────────────────────
# Logging
# ─────────────────────────────────────────────────────────────────────────────
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
)
# Suppress noisy httpx logs
logging.getLogger("httpx").setLevel(logging.WARNING)
logger = logging.getLogger("vision-service")

# ─────────────────────────────────────────────────────────────────────────────
# AWS Clients — only created when NOT in testing mode
# ─────────────────────────────────────────────────────────────────────────────
s3_client = None
sqs_client = None

if not config.TESTING_MODE:
    if config.AWS_REGION:
        s3_client  = boto3.client("s3",  region_name=config.AWS_REGION)
        sqs_client = boto3.client("sqs", region_name=config.AWS_REGION)
    else:
        logger.warning("AWS_REGION not set — S3/SQS clients not initialised")
else:
    logger.warning("⚠️  TESTING MODE: All AWS (S3 / SQS) calls are DISABLED. No cloud costs.")

# ─────────────────────────────────────────────────────────────────────────────
# AI Model Registry (always loaded — local compute, not AWS)
# ─────────────────────────────────────────────────────────────────────────────
logger.info("Loading object detection models into registry... (this may take a moment)")
MODEL_REGISTRY = {
    "human-detection-v1": pipeline("object-detection", model="hustvl/yolos-tiny"),
}
logger.info("✅ Models loaded")

# Global lock to prevent multiple AI inferences thrashing the CPU
ai_lock = threading.Lock()

# ─────────────────────────────────────────────────────────────────────────────
# RTSP engine state
# ─────────────────────────────────────────────────────────────────────────────
api_client: ViolationApiClient    = None
stream_manager: CameraStreamManager = None
main_loop: asyncio.AbstractEventLoop = None

# Global pause flag — when True, on_frame() is a no-op even if streams keep reading
_streams_paused: bool = False


def _apply_config(cameras: list) -> list:
    for cam in cameras:
        cam.target_fps           = config.TARGET_FPS
        cam.frame_timeout_seconds = config.FRAME_TIMEOUT_SECONDS
    return cameras


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
    global _streams_paused, violation_manager

    if _streams_paused:
        logger.warning("[%s] ⏸️  on_frame: streams are PAUSED - skipping", cam.camera_id)
        return

    try:
        # ── DIAGNOSTIC: confirm on_frame is being called ──────────────────────
        num_rules = len(cam.violation_rules)
        logger.info("[%s] 🔍 on_frame called | rules=%d | paused=%s",
                    cam.camera_id, num_rules, _streams_paused)

        if num_rules == 0:
            logger.warning("[%s] ⚠️  No violation rules configured for this camera — skipping AI inference. "
                           "Check that a SOP is assigned in the admin panel.", cam.camera_id)
            return

        # 1. Local AI Inference
        target_size = (320, 240)
        resized_frame = cv2.resize(frame, target_size)
        rgb_frame = cv2.cvtColor(resized_frame, cv2.COLOR_BGR2RGB)
        pil_image = Image.fromarray(rgb_frame)

        with ai_lock:
            results = []
            # Extract unique model identifiers across all rules
            unique_models = {rule.model_identifier for rule in cam.violation_rules}
            logger.info("[%s] 🤖 Running models: %s", cam.camera_id, unique_models)
            
            for model_id in unique_models:
                model = MODEL_REGISTRY.get(model_id)
                if model:
                    try:
                        detections = model(pil_image)
                        logger.info("[%s] 📊 Model '%s' returned %d detection(s): %s",
                                    cam.camera_id, model_id, len(detections),
                                    [(d['label'], round(d['score'], 2)) for d in detections])
                        for d in detections:
                            d["source_model"] = model_id
                        results.extend(detections)
                    except Exception as e:
                        logger.error("[%s] Model inference failed for model %s: %s", cam.camera_id, model_id, e)
                else:
                    logger.error("[%s] ❌ Model '%s' NOT FOUND in MODEL_REGISTRY! Available: %s",
                                 cam.camera_id, model_id, list(MODEL_REGISTRY.keys()))

        # 2. State Management & Deduplication
        if violation_manager is None:
            logger.error("[%s] violation_manager is None!", cam.camera_id)
            return

        future = asyncio.run_coroutine_threadsafe(
            violation_manager.process_frame(cam.camera_id, results, cam.violation_rules),
            main_loop
        )
        actions = future.result() # Wait for state decision

        if not actions:
            return

        # 3. Handle Actions (New Violation or Update Existing)
        for action in actions:
            status = action["StateStatus"] # "New" or "Update"
            det = action["Metadata"]
            track_id = action["TrackId"]

            # Draw on frame for both new and updates (visual feedback)
            box = det["box"]
            orig_h, orig_w = frame.shape[:2]
            h_scale = orig_h / target_size[1]
            w_scale = orig_w / target_size[0]
            
            xmin, ymin = int(box["xmin"] * w_scale), int(box["ymin"] * h_scale)
            xmax, ymax = int(box["xmax"] * w_scale), int(box["ymax"] * h_scale)
            
            color = (0, 0, 255) # Red for active violations
            cv2.rectangle(frame, (xmin, ymin), (xmax, ymax), color, 3)
            cv2.putText(frame, f"ID:{track_id} {det['label']} {det['score']:.2f}", (xmin, ymin - 10),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, color, 2)

            if status == "New":
                # Handle S3 upload only for NEW events to save bandwidth/cost
                frame_url = ""
                if not config.TESTING_MODE and s3_client and config.S3_BUCKET_NAME:
                    annotated_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                    pil_save = Image.fromarray(annotated_rgb)
                    filename = f"violations/{cam.tenant_id}/{cam.camera_id}/{datetime.utcnow().strftime('%Y-%m-%d')}/{uuid.uuid4()}.jpg"
                    buf = io.BytesIO()
                    pil_save.save(buf, format="JPEG")
                    buf.seek(0)
                    try:
                        s3_client.put_object(Bucket=config.S3_BUCKET_NAME, Key=filename, Body=buf, ContentType="image/jpeg")
                        frame_url = f"https://{config.S3_BUCKET_NAME}.s3.{config.AWS_REGION}.amazonaws.com/{filename}"
                    except Exception as e:
                        logger.warning("[%s] S3 upload failed: %s", cam.camera_id, e)

                payload = {
                    "TenantId": cam.tenant_id,
                    "CameraId": cam.camera_db_id,
                    "ModelIdentifier": action.get("ModelIdentifier"),
                    "CorrelationId": str(uuid.uuid4()),
                    "TrackId": track_id,
                    "Timestamp": datetime.utcnow().isoformat(),
                    "FramePath": frame_url,
                    "Status": "Pending",
                    "MetadataJson": json.dumps(det),
                }
                future = asyncio.run_coroutine_threadsafe(api_client.post_violation(payload), main_loop)
                def _post_done(f):
                    try:
                        f.result()
                    except Exception as e:
                        logger.error("[%s] ❌ post_violation crashed silently: %s", cam.camera_id, e)
                future.add_done_callback(_post_done)
                logger.info("[%s] 🚨 NEW Violation Event created for Track %d", cam.camera_id, track_id)

            elif status == "Update":
                # For updates, we just refresh the timestamp in the DB
                # First, find the active event ID for this track (cached by manager or fetched from API)
                timestamp = datetime.utcnow().isoformat()
                
                async def update_async():
                    active_v = await api_client.get_active_violation(cam.camera_db_id, track_id)
                    if active_v and "id" in active_v:
                        await api_client.update_violation(active_v["id"], timestamp)
                        logger.debug("[%s] Updated last_seen for Track %d (Event: %s)", cam.camera_id, track_id, active_v["id"])
                    else:
                        # Fallback if event missing: could happen on service restart
                        pass

                future = asyncio.run_coroutine_threadsafe(update_async(), main_loop)
                def _update_done(f):
                    try:
                        f.result()
                    except Exception as e:
                        logger.error("[%s] ❌ update_violation crashed silently: %s", cam.camera_id, e)
                future.add_done_callback(_update_done)

    except Exception as e:
        logger.error("[%s] on_frame error: %s", cam.camera_id, e)



# ─────────────────────────────────────────────────────────────────────────────
# Background: Camera Poll Loop
# ─────────────────────────────────────────────────────────────────────────────

# ─────────────────────────────────────────────────────────────────────────────
# Removed: Camera Poll Loop. Replaced with Webhook POST /streams/reload
# ─────────────────────────────────────────────────────────────────────────────


# ─────────────────────────────────────────────────────────────────────────────
# FastAPI Lifespan
# ─────────────────────────────────────────────────────────────────────────────

@asynccontextmanager
async def lifespan(app: FastAPI):
    global api_client, stream_manager, violation_manager, main_loop
    main_loop = asyncio.get_running_loop()

    config.log_config(logger)

    api_client = ViolationApiClient(
        base_url=config.VIOLATION_API_BASE_URL,
        api_key=config.INTERNAL_API_KEY,
    )
    stream_manager = CameraStreamManager(
        frame_callback=on_frame,
        max_workers=config.MAX_STREAM_WORKERS,
    )
    
    violation_manager = ViolationManager()

    cameras = await api_client.fetch_active_cameras()
    cameras = _apply_config(cameras)

    logger.info("⏸️  Vision Engine started in IDLE mode. Streams will start on first camera add/reload webhook.")

    yield  # ← Service is running

    logger.info("🛑 Shutting down...")
    await stream_manager.stop_all()
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


@app.post("/streams/pause", tags=["RTSP Engine"])
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


@app.post("/streams/resume", tags=["RTSP Engine"])
async def resume_streams():
    """Resume frame processing after a pause."""
    global _streams_paused
    _streams_paused = False
    logger.info("▶️  All stream processing RESUMED")
    return {"paused": False, "message": "All streams resumed."}


@app.post("/streams/reload", tags=["RTSP Engine"])
async def reload_streams():
    """Hot-reload cameras from the Violation API: adds new, removes deleted, keeps running streams."""
    if stream_manager is None or api_client is None:
        return JSONResponse(status_code=503, content={"error": "Not initialised"})
    cameras = await api_client.fetch_active_cameras()
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


@app.post("/streams/test", tags=["RTSP Engine"])
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
            <div class="form-group"><label>Camera ID:</label><input type="text" id="cameraId" value="TestCamera1"></div>
            <div class="form-group"><label>Tenant ID:</label><input type="text" id="tenantId" value="d34493e3-612e-42d2-b896-37fa72e53ee0"></div>
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


@app.post("/analyze", tags=["Testing"])
async def analyze_image(
    file:      UploadFile = File(...),
    camera_id: str        = Form("TestCamera1"),
    tenant_id: str        = Form("d34493e3-612e-42d2-b896-37fa72e53ee0"),
):
    """
    [TEST] Upload a single image frame for immediate analysis.
    Respects TESTING_MODE: skips S3/SQS in testing, uses them in production.
    """
    try:
        image_data = await file.read()
        image      = Image.open(io.BytesIO(image_data))
        
        # Test mode uses first available registry model for UI testing
        model_id = list(MODEL_REGISTRY.keys())[0] if MODEL_REGISTRY else "Security"
        model = MODEL_REGISTRY[model_id] if MODEL_REGISTRY else None
        results = model(image) if model else []

        draw          = ImageDraw.Draw(image)
        has_violation = False
        violations    = []

        for d in results:
            if d["score"] > 0.7:
                box   = d["box"]
                color = "red" if d["label"] == "person" else "green"
                draw.rectangle(
                    [box["xmin"], box["ymin"], box["xmax"], box["ymax"]],
                    outline=color, width=3,
                )
                draw.text((box["xmin"], box["ymin"]), f"{d['label']} {d['score']:.2f}", fill=color)
                if d["label"] == "person":
                    has_violation = True
                    violations.append(d)

        frame_url  = ""
        sqs_status = "Skipped"

        if has_violation and not config.TESTING_MODE:
            # S3 upload
            if s3_client and config.S3_BUCKET_NAME:
                filename = (
                    f"violations/{tenant_id}/{camera_id}/"
                    f"{datetime.utcnow().strftime('%Y-%m-%d')}/{uuid.uuid4()}.jpg"
                )
                buf = io.BytesIO()
                image.save(buf, format="JPEG")
                buf.seek(0)
                try:
                    s3_client.put_object(
                        Bucket=config.S3_BUCKET_NAME,
                        Key=filename, Body=buf, ContentType="image/jpeg",
                    )
                    frame_url = f"https://{config.S3_BUCKET_NAME}.s3.{config.AWS_REGION}.amazonaws.com/{filename}"
                except Exception as e:
                    return JSONResponse(status_code=500, content={"error": f"S3 Failed: {e}"})

            # SQS
            if sqs_client and config.SQS_QUEUE_URL:
                payload = {
                    "TenantId": tenant_id,  "Type": "Security",
                    "ModelIdentifier": model_id,
                    "CorrelationId": str(uuid.uuid4()),
                    "Timestamp": datetime.utcnow().isoformat(),
                    "FramePath": frame_url, "CameraId": camera_id,
                    "Severity": "High",     "Status": "Pending",
                    "MetadataJson": json.dumps(results),
                }
                try:
                    sqs_client.send_message(QueueUrl=config.SQS_QUEUE_URL, MessageBody=json.dumps(payload))
                    sqs_status = "Sent"
                except Exception as e:
                    return JSONResponse(status_code=500, content={"error": f"SQS Failed: {e}"})
        elif has_violation and config.TESTING_MODE:
            sqs_status = "Skipped (TESTING_MODE)"

        return {
            "testing_mode":      config.TESTING_MODE,
            "filename":          file.filename,
            "detections":        results,
            "violation_detected": has_violation,
            "violations":        violations,
            "frame_url":         frame_url or "(not uploaded — testing mode)",
            "sqs_status":        sqs_status,
        }

    except Exception as e:
        return JSONResponse(status_code=500, content={"error": str(e)})




# ─────────────────────────────────────────────────────────────────────────────
# Health
# ─────────────────────────────────────────────────────────────────────────────

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
