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
INTERNAL_API_KEY: str       = os.environ.get("INTERNAL_API_KEY") or "alpha-vision-internal"
CLOUDFLARE_API_TOKEN: str   = os.environ.get("CLOUDFLARE_API_TOKEN", "")

# ─── Cloudinary (debug frame uploads) ───────────────────────────────────────
CLOUDINARY_CLOUD_NAME: str = os.environ.get("CLOUDINARY_CLOUD_NAME", "")
CLOUDINARY_API_KEY: str    = os.environ.get("CLOUDINARY_API_KEY", "")
CLOUDINARY_API_SECRET: str = os.environ.get("CLOUDINARY_API_SECRET", "")

# ─── Roboflow Inference API ──────────────────────────────────────────────────
ROBOFLOW_API_KEY: str = os.environ.get("ROBOFLOW_API_KEY", "dummy_key_please_replace")

# ─── RTSP Stream Engine ───────────────────────────────────────────────────────
TARGET_FPS: float               = float(os.environ.get("TARGET_FPS", "1.0"))
FRAME_TIMEOUT_SECONDS: float    = float(os.environ.get("FRAME_TIMEOUT_SECONDS", "30.0"))
CAMERA_POLL_INTERVAL_SECONDS: int = int(os.environ.get("CAMERA_POLL_INTERVAL_SECONDS", "60"))
MAX_STREAM_WORKERS: int         = int(os.environ.get("MAX_STREAM_WORKERS", "500"))
# NOTE: Set to false for live RTSP cameras. True is only for offline MP4 file playback.
SIMULATE_REALTIME_PLAYBACK: bool = os.environ.get("SIMULATE_REALTIME_PLAYBACK", "false").lower() == "true"

# ─── Inference Tuning ────────────────────────────────────────────────────────
MIN_CONFIDENCE_ROBOFLOW: float = float(os.environ.get("MIN_CONFIDENCE_ROBOFLOW", "0.60"))
MIN_CONFIDENCE_HUGGINGFACE: float = float(os.environ.get("MIN_CONFIDENCE_HUGGINGFACE", "0.40"))

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
    if not TESTING_MODE:
        logger.info("  S3 Bucket        : %s", S3_BUCKET_NAME or "NOT SET")
        logger.info("  SQS Queue        : %s", SQS_QUEUE_URL or "NOT SET")
        logger.info("  AWS Region       : %s", AWS_REGION or "NOT SET")
    logger.info("=" * 60)
