"""
config.py — Centralised configuration for Vision Inference Service
===================================================================
Reads from environment variables (set by AppHost in production,
or from .env file in local standalone mode).

Import this module instead of scattered os.environ.get() calls.
"""
import os
from dotenv import load_dotenv  # python-dotenv; safe no-op if file not present

# Load .env for standalone local runs.
# When run via AppHost, env vars are already injected — load_dotenv() won't
# override them (since os.environ takes precedence by default).
load_dotenv()

# ─── Server ──────────────────────────────────────────────────────────────────
PORT: int = int(os.environ.get("PORT", "8000"))

# ─── AWS ─────────────────────────────────────────────────────────────────────
SQS_QUEUE_URL: str  = os.environ.get("SQS_QUEUE_URL", "")
S3_BUCKET_NAME: str = os.environ.get("S3_BUCKET_NAME", "")
AWS_REGION: str     = os.environ.get("AWS_REGION", "")

# ─── Testing Mode ────────────────────────────────────────────────────────────
# When True: AI inference runs locally, but ALL AWS calls (S3/SQS/SES) are
# skipped. Violations are printed to the console log instead.
# Safe to leave running overnight — zero AWS cost.
TESTING_MODE: bool = os.environ.get("TESTING_MODE", "false").lower() == "true"

# ─── Violation API (service-to-service) ──────────────────────────────────────
# AppHost sets this automatically because violation-management-api has a fixed
# http endpoint on port 5001. For standalone runs, .env provides the default.
VIOLATION_API_BASE_URL: str = os.environ.get("VIOLATION_API_BASE_URL") or "http://localhost:5001"

# D-5 fix: no hardcoded fallback — a known default in source code is a
# security liability (anyone who reads the repo can forge API calls).
# In TESTING_MODE the key is never used (violations aren't posted), so a
# warning is sufficient.  In production mode, fail loudly at startup.
INTERNAL_API_KEY: str = os.environ.get("INTERNAL_API_KEY", "")
if not INTERNAL_API_KEY:
    if TESTING_MODE:
        import warnings
        warnings.warn(
            "INTERNAL_API_KEY is not set (TESTING_MODE=true). "
            "Violation POST calls are skipped, so this is safe for local testing. "
            "Set INTERNAL_API_KEY in your .env file before switching to production mode.",
            stacklevel=1,
        )
    else:
        raise RuntimeError(
            "INTERNAL_API_KEY environment variable is not set. "
            "Add it to your .env file or to your deployment environment secrets. "
            "It must match the InternalApiKey value configured in violation-management-api."
        )
CLOUDFLARE_API_TOKEN: str   = os.environ.get("CLOUDFLARE_API_TOKEN", "")

# ─── Cloudinary (debug frame uploads) ───────────────────────────────────────
CLOUDINARY_CLOUD_NAME: str = os.environ.get("CLOUDINARY_CLOUD_NAME", "")
CLOUDINARY_API_KEY: str    = os.environ.get("CLOUDINARY_API_KEY", "")
CLOUDINARY_API_SECRET: str = os.environ.get("CLOUDINARY_API_SECRET", "")

# ─── Roboflow Inference API ──────────────────────────────────────────────────
ROBOFLOW_API_KEY: str = os.environ.get("ROBOFLOW_API_KEY", "dummy_key_please_replace")

# Restaurant PPE YOLOv11 model exported from Roboflow.
# This is the only supported path for restaurant hairnet/mask compliance.
RESTAURANT_PPE_MODEL_IDENTIFIER: str = os.environ.get("RESTAURANT_PPE_MODEL_IDENTIFIER", "restaurant-ppe-v1")
RESTAURANT_PPE_MODEL_PATH: str = os.environ.get("RESTAURANT_PPE_MODEL_PATH", "/tmp/models/restaurant-ppe-yolo11m-v2.pt")
KITCHEN_HYGIENE_YOLO11N_MODEL_IDENTIFIER: str = os.environ.get(
    "KITCHEN_HYGIENE_YOLO11N_MODEL_IDENTIFIER", "kitchen-hygiene-yolo11n-v1"
)
KITCHEN_HYGIENE_YOLO11M_MODEL_IDENTIFIER: str = os.environ.get(
    "KITCHEN_HYGIENE_YOLO11M_MODEL_IDENTIFIER", "kitchen-hygiene-yolo11m-v1"
)
KITCHEN_HYGIENE_YOLO11M_V2_MODEL_IDENTIFIER: str = os.environ.get(
    "KITCHEN_HYGIENE_YOLO11M_V2_MODEL_IDENTIFIER", "kitchen-hygiene-yolo11m-v2"
)
KITCHEN_HYGIENE_YOLO11N_MODEL_PATH: str = os.environ.get(
    "KITCHEN_HYGIENE_YOLO11N_MODEL_PATH", "/tmp/models/kitchen-hygiene-yolo11n.pt"
)
KITCHEN_HYGIENE_YOLO11M_MODEL_PATH: str = os.environ.get(
    "KITCHEN_HYGIENE_YOLO11M_MODEL_PATH", "/tmp/models/kitchen-hygiene-yolo11m.pt"
)
KITCHEN_HYGIENE_YOLO11M_V2_MODEL_PATH: str = os.environ.get(
    "KITCHEN_HYGIENE_YOLO11M_V2_MODEL_PATH", "/tmp/models/kitchen-hygiene-yolo11m-v2.pt"
)
RESTAURANT_PPE_IMAGE_SIZE: int = int(os.environ.get("RESTAURANT_PPE_IMAGE_SIZE", "640"))
# CLAHE + conditional gamma low-light preprocessing applied to every PPE frame.
# Set to "false" to A/B compare recall against raw input.
RESTAURANT_PPE_ENHANCE_LOWLIGHT: bool = os.environ.get(
    "RESTAURANT_PPE_ENHANCE_LOWLIGHT", "true"
).lower() == "true"

# Person-crop pre-layer: when true, every restaurant-ppe inference is preceded
# by a YOLO11n person detection pass. The PPE model then runs on each padded
# person crop instead of the full frame. This dramatically improves recall for
# mask/hairnet on wide-angle CCTV scenes (a face occupying 60x70 px in the full
# frame becomes 300+ px in the crop). If no persons are detected the frame is
# skipped entirely — no PPE inference, no false positives on empty/pest scenes.
RESTAURANT_PPE_PERSON_CROP: bool = os.environ.get(
    "RESTAURANT_PPE_PERSON_CROP", "true"
).lower() == "true"
PERSON_DETECTOR_CONFIDENCE: float = float(os.environ.get("PERSON_DETECTOR_CONFIDENCE", "0.25"))
# Padding ratio applied to each person bbox before cropping. 0.15 = +15% each side.
# Padding ensures hairnets above the head and gloves below the wrist aren't clipped.
PERSON_CROP_PADDING: float = float(os.environ.get("PERSON_CROP_PADDING", "0.15"))

# Motion gate — skip person re-detection when consecutive frames are visually
# almost identical (e.g. empty porch at 3am). When enabled, the inference
# engine computes mean absolute pixel diff between this frame and the last
# one cached per camera; if below `MOTION_GATE_THRESHOLD` it reuses the
# previous frame's person_boxes instead of re-running YOLOv11n. Cuts CPU
# 3-5x on static cameras. Off by default so it never silently masks a real
# detection regression — turn on per deployment after verifying recall.
MOTION_GATE_ENABLED: bool = os.environ.get("MOTION_GATE_ENABLED", "false").lower() == "true"
MOTION_GATE_THRESHOLD: float = float(os.environ.get("MOTION_GATE_THRESHOLD", "5.0"))
MOTION_GATE_SAMPLE_SIZE: int = int(os.environ.get("MOTION_GATE_SAMPLE_SIZE", "160"))

# ─── S3 Model Storage ─────────────────────────────────────────────────────────
# Bucket that stores exported model weights.
# At startup the inference engine downloads the model from S3 if it is not
# already cached at RESTAURANT_PPE_MODEL_PATH.
MODEL_S3_BUCKET: str = os.environ.get("MODEL_S3_BUCKET", "restaurant-ppe-yolo11-pt4-v1--use1-az4--x-s3")
MODEL_S3_KEY: str    = os.environ.get("MODEL_S3_KEY", "models/restaurant-ppe-yolo11m-v2.pt")
KITCHEN_HYGIENE_YOLO11N_S3_KEY: str = os.environ.get(
    "KITCHEN_HYGIENE_YOLO11N_S3_KEY", "models/kitchen-hygiene-yolo11n.pt"
)
KITCHEN_HYGIENE_YOLO11M_S3_KEY: str = os.environ.get(
    "KITCHEN_HYGIENE_YOLO11M_S3_KEY", "models/kitchen-hygiene-yolo11m.pt"
)
KITCHEN_HYGIENE_YOLO11M_V2_S3_KEY: str = os.environ.get(
    "KITCHEN_HYGIENE_YOLO11M_V2_S3_KEY", "models/kitchen-hygiene-yolo11m-v2.pt"
)

# ─── Pest Detection Model (S3) ────────────────────────────────────────────────
PEST_MODEL_IDENTIFIER: str = os.environ.get("PEST_MODEL_IDENTIFIER", "pest-detection-v1")
PEST_MODEL_PATH: str       = os.environ.get("PEST_MODEL_PATH", "/tmp/models/kitchen-pest-yolo11m.pt")
PEST_MODEL_S3_KEY: str     = os.environ.get("PEST_MODEL_S3_KEY", "models/kitchen-pest-yolo11m.pt")
PEST_MODEL_IMAGE_SIZE: int = int(os.environ.get("PEST_MODEL_IMAGE_SIZE", "640"))
# Pest detections need a lower threshold — pests are small and often partially occluded.
# Does NOT use person-crop gate; runs on full frame since pests appear in the environment.

# ─── RTSP Stream Engine ───────────────────────────────────────────────────────
TARGET_FPS: float               = float(os.environ.get("TARGET_FPS", "1.0"))
FRAME_TIMEOUT_SECONDS: float    = float(os.environ.get("FRAME_TIMEOUT_SECONDS", "30.0"))
CAMERA_POLL_INTERVAL_SECONDS: int = int(os.environ.get("CAMERA_POLL_INTERVAL_SECONDS", "60"))
MAX_STREAM_WORKERS: int         = int(os.environ.get("MAX_STREAM_WORKERS", "500"))
MAX_STREAM_LAG_SECONDS: float   = float(os.environ.get("MAX_STREAM_LAG_SECONDS", "5.0"))
# NOTE: Set to false for live RTSP cameras. True is only for offline MP4 file playback.
SIMULATE_REALTIME_PLAYBACK: bool = os.environ.get("SIMULATE_REALTIME_PLAYBACK", "false").lower() == "true"

# Interval at which the service polls the Violation API for camera config changes.
# Any camera added/removed/reassigned in the dashboard takes effect within this window.
# Default: 3600s (1 hour). Set lower in dev (e.g. 60) for faster feedback.
CONFIG_POLL_INTERVAL_SECONDS: int = int(os.environ.get("CONFIG_POLL_INTERVAL_SECONDS", "3600"))

# ─── Edge Device Identity ────────────────────────────────────────────────────
# When multiple vision-inference services run for the same tenant (large
# camera fleets split across edge devices), each must identify itself so the
# Violation API can hand back the correct subset of cameras.
#
# Identifier resolution priority (rtsp/device_identity.py):
#   1. DEVICE_ID env var          — explicit override (EKS / Docker / k8s secret)
#   2. DEVICE_IDENTIFIER_FILE     — persisted UUID written on first boot
#   3. Generated UUID4            — saved to DEVICE_IDENTIFIER_FILE
#
# TENANT_ID must be set when DEVICE_ID is set — the API rejects registration
# without it. In single-device dev/testing setups (no DEVICE_ID) the service
# falls back to the legacy "all active cameras" behaviour.
DEVICE_ID: str = os.environ.get("DEVICE_ID", "")
DEVICE_IDENTIFIER_FILE: str = os.environ.get("DEVICE_IDENTIFIER_FILE", ".alpha_device_id")
DEVICE_DISPLAY_NAME: str = os.environ.get("DEVICE_DISPLAY_NAME", "")
DEVICE_TENANT_ID: str = os.environ.get("DEVICE_TENANT_ID", "")

# ─── Inference Tuning ────────────────────────────────────────────────────────
MIN_CONFIDENCE_ROBOFLOW: float = float(os.environ.get("MIN_CONFIDENCE_ROBOFLOW", "0.60"))
MIN_CONFIDENCE_HUGGINGFACE: float = float(os.environ.get("MIN_CONFIDENCE_HUGGINGFACE", "0.40"))
# Restaurant PPE (mask / gloves / hairnet) — must meet this score or the
# detection is suppressed before any violation logic runs.
# 0.60 is the documented production minimum (see Readme.md § Configuration).
# Below 0.55 the YOLOv11n model produces too many false positives on small
# faces in wide-angle CCTV, especially for no-mask and no-hairnet classes.
MIN_CONFIDENCE_RESTAURANT_PPE: float = float(os.environ.get("MIN_CONFIDENCE_RESTAURANT_PPE", "0.65"))
MIN_CONFIDENCE_PEST: float           = float(os.environ.get("MIN_CONFIDENCE_PEST", "0.50"))

# ─── Startup Summary ─────────────────────────────────────────────────────────
def log_config(logger) -> None:
    """Print a startup config summary to the given logger."""
    mode_label = "⚠️  TESTING (AWS disabled)" if TESTING_MODE else "🚀 PRODUCTION (AWS enabled)"
    logger.info("=" * 60)
    logger.info("  Mode             : %s", mode_label)
    logger.info("  Violation API    : %s", VIOLATION_API_BASE_URL)
    logger.info("  Target FPS       : %.1f", TARGET_FPS)
    logger.info("  Frame Timeout    : %.1fs", FRAME_TIMEOUT_SECONDS)
    logger.info("  Poll Interval    : %ds", CAMERA_POLL_INTERVAL_SECONDS)
    logger.info("  Max Workers      : %d", MAX_STREAM_WORKERS)
    logger.info("  Device Tenant    : %s", DEVICE_TENANT_ID or "(none — single-device mode)")
    logger.info("  Device ID (env)  : %s", DEVICE_ID or "(auto — file/UUID)")
    logger.info("  Restaurant PPE   : %s", RESTAURANT_PPE_MODEL_PATH or "NOT SET")
    if not TESTING_MODE:
        logger.info("  S3 Bucket        : %s", S3_BUCKET_NAME or "NOT SET")
        logger.info("  SQS Queue        : %s", SQS_QUEUE_URL or "NOT SET")
        logger.info("  AWS Region       : %s", AWS_REGION or "NOT SET")
    logger.info("=" * 60)
