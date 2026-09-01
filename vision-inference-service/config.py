"""
config.py — Centralised configuration for Vision Inference Service
===================================================================
Reads from environment variables (set by AppHost in production,
or from .env file in local standalone mode).

Import this module instead of scattered os.environ.get() calls.
"""
import os
from dotenv import load_dotenv, dotenv_values  # python-dotenv; safe no-op if file not present

# Dotenv precedence (highest wins):
#   1. Real process environment (AppHost / Docker / K8s injected vars)
#   2. .env.local  — explicit, documented local override file
#   3. .env        — checked-in / deployment defaults
#
# V1 fix: previously `.env.local` was loaded BEFORE `.env`, relying on
# python-dotenv's "never override" default. The net effect (.env.local wins
# over .env) was undocumented and easy to invert by reordering the two calls.
# We now load `.env` first, then apply `.env.local` as an explicit override —
# but only for keys that were NOT already present in the real process
# environment, so injected env vars always win over BOTH files.
_pre_existing_env_keys = set(os.environ.keys())
load_dotenv()  # .env — fills only keys not already in os.environ
for _k, _v in (dotenv_values(".env.local") or {}).items():
    if _v is not None and _k not in _pre_existing_env_keys:
        os.environ[_k] = _v  # .env.local overrides .env, never the real env
del _pre_existing_env_keys

# ─── Server ──────────────────────────────────────────────────────────────────
PORT: int = int(os.environ.get("PORT", "8000"))

# Root logging level for the service (INFO by default; set LOG_LEVEL=DEBUG to
# get per-frame diagnostics). Previously main.py hard-coded DEBUG which
# floods disks in production.
LOG_LEVEL: str = os.environ.get("LOG_LEVEL", "INFO").strip().upper()

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
# H4 fix: the legacy `http://localhost:5001` fallback caused production pods
# that forgot to set the env var to silently swallow violations. In production
# we require an explicit value; testing mode keeps the localhost convenience.
_violation_api_default = "http://localhost:5001" if TESTING_MODE else ""
VIOLATION_API_BASE_URL: str = os.environ.get("VIOLATION_API_BASE_URL") or _violation_api_default
if not VIOLATION_API_BASE_URL:
    raise RuntimeError(
        "VIOLATION_API_BASE_URL environment variable is not set. "
        "In production this must point at the Violation Management API; there is no safe default."
    )

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

# C4 fix: known weak/sample keys must never be accepted in production. Anyone
# who has skimmed the repo or the dev .env knows this string, so accepting it
# in production is equivalent to having no auth at all.
_WEAK_INTERNAL_API_KEYS = {
    "",
    "alpha-vision-internal",
    "changeme",
    "please-change-me",
    "dev",
    "test",
    "secret",
    "dummy_key_please_replace",
}
if INTERNAL_API_KEY and not TESTING_MODE and INTERNAL_API_KEY.strip().lower() in _WEAK_INTERNAL_API_KEYS:
    raise RuntimeError(
        "INTERNAL_API_KEY is set to a known weak/sample value. "
        "Generate a fresh value (e.g. `openssl rand -hex 32`) for production deployments."
    )

# C5 fix: when this endpoint is publicly reachable (i.e. not behind a private
# network ACL) we require the same shared secret used for cross-service auth
# to be presented on the management endpoints (/analyze, /streams/*, /feedback).
# Operators can disable this in trusted local environments by exporting
# REQUIRE_INTERNAL_API_KEY=false, but it defaults to ON to be safe-by-default.
REQUIRE_INTERNAL_API_KEY: bool = os.environ.get(
    "REQUIRE_INTERNAL_API_KEY", "true" if not TESTING_MODE else "false"
).lower() == "true"

CLOUDFLARE_API_TOKEN: str   = os.environ.get("CLOUDFLARE_API_TOKEN", "")

# C7 fix: the WHIP publish URL is interpolated into FFmpeg argv together with
# the Bearer token. If the upstream config DB is ever tampered with, an
# attacker-controlled WHIP URL would receive our long-lived Cloudflare API
# token. Restricting the host to a known allow-list contains that blast
# radius. Comma-separated list of allowed suffixes (case-insensitive match
# against the URL hostname).
CLOUDFLARE_WHIP_ALLOWED_HOSTS: tuple = tuple(
    h.strip().lower()
    for h in os.environ.get(
        "CLOUDFLARE_WHIP_ALLOWED_HOSTS",
        "cloudflare.com,cloudflarestream.com,videodelivery.net",
    ).split(",")
    if h.strip()
)

# ─── Cloudinary (debug frame uploads) ───────────────────────────────────────
CLOUDINARY_CLOUD_NAME: str = os.environ.get("CLOUDINARY_CLOUD_NAME", "")
CLOUDINARY_API_KEY: str    = os.environ.get("CLOUDINARY_API_KEY", "")
CLOUDINARY_API_SECRET: str = os.environ.get("CLOUDINARY_API_SECRET", "")
# C6 fix: the per-camera 30s Cloudinary upload is a debugging aid that, when
# left enabled in production with many cameras, burns the Cloudinary free tier
# in hours and spawns unbounded daemon threads on network stalls. Off by
# default — only enable explicitly when actively diagnosing a stream.
ENABLE_DEBUG_FRAME_UPLOADS: bool = os.environ.get(
    "ENABLE_DEBUG_FRAME_UPLOADS", "false"
).lower() == "true"

# ─── Roboflow Inference API ──────────────────────────────────────────────────
ROBOFLOW_API_KEY: str = os.environ.get("ROBOFLOW_API_KEY", "")
# H6 fix: reject the placeholder value at startup in production. A pod running
# with the placeholder silently issues 401s for every Roboflow detection.
if ROBOFLOW_API_KEY.strip().lower() in {"dummy_key_please_replace", "changeme"} and not TESTING_MODE:
    raise RuntimeError(
        "ROBOFLOW_API_KEY is set to a placeholder value. "
        "Replace it with a real Roboflow API key or unset it for production."
    )

# ─── Dynamic AI Model Storage & Cache Directory ──────────────────────────────
# All AI model artifacts (S3 bucket, S3 key, Download URLs, Confidence, Image Size,
# Cropping, Human Gating) are dynamically provided by the AI Model Library database
# and downloaded/verified on demand.
MODEL_CACHE_DIR: str = os.environ.get(
    "MODEL_CACHE_DIR",
    os.path.expanduser("~/.alpha-surveillance/model_cache/models")
)
DEFAULT_MODEL_IMAGE_SIZE: int = int(os.environ.get("DEFAULT_MODEL_IMAGE_SIZE", "640"))
DEFAULT_MODEL_CONFIDENCE: float = float(os.environ.get("DEFAULT_MODEL_CONFIDENCE", "0.50"))

# CLAHE + conditional gamma low-light preprocessing applied to PPE frames.
RESTAURANT_PPE_ENHANCE_LOWLIGHT: bool = os.environ.get(
    "RESTAURANT_PPE_ENHANCE_LOWLIGHT", "true"
).lower() == "true"

# Person-crop pre-layer heuristics:
RESTAURANT_PPE_PERSON_CROP: bool = os.environ.get(
    "RESTAURANT_PPE_PERSON_CROP", "true"
).lower() == "true"
PERSON_DETECTOR_CONFIDENCE: float = float(os.environ.get("PERSON_DETECTOR_CONFIDENCE", "0.20"))
RESTAURANT_PPE_FALLBACK_FULL_FRAME_ON_NO_PERSON: bool = os.environ.get(
    "RESTAURANT_PPE_FALLBACK_FULL_FRAME_ON_NO_PERSON", "true"
).lower() == "true"
PERSON_CROP_PADDING: float = float(os.environ.get("PERSON_CROP_PADDING", "0.15"))

# Experimental Locate-Anything / open-vocabulary grounding path.
LOCATE_ANYTHING_MODEL_REFERENCE: str = os.environ.get(
    "LOCATE_ANYTHING_MODEL_REFERENCE", "google/owlv2-base-patch16-ensemble"
)

# Motion gate — skip person re-detection when consecutive frames are visually almost identical.
MOTION_GATE_ENABLED: bool = os.environ.get("MOTION_GATE_ENABLED", "false").lower() == "true"
MOTION_GATE_THRESHOLD: float = float(os.environ.get("MOTION_GATE_THRESHOLD", "5.0"))
MOTION_GATE_SAMPLE_SIZE: int = int(os.environ.get("MOTION_GATE_SAMPLE_SIZE", "160"))

# ─── RTSP Stream Engine ───────────────────────────────────────────────────────
TARGET_FPS: float               = float(os.environ.get("TARGET_FPS", "1.0"))
# L4 fix: upper bound applied by main.py / stream_client.py when a per-camera
# `target_fps` override comes from the DB. Without this clamp a misconfigured
# camera can request 1000 FPS and exhaust CPU/GPU budget.
MAX_TARGET_FPS: float           = float(os.environ.get("MAX_TARGET_FPS", "30.0"))
FRAME_TIMEOUT_SECONDS: float    = float(os.environ.get("FRAME_TIMEOUT_SECONDS", "30.0"))
CAMERA_POLL_INTERVAL_SECONDS: int = int(os.environ.get("CAMERA_POLL_INTERVAL_SECONDS", "60"))
MAX_STREAM_WORKERS: int         = int(os.environ.get("MAX_STREAM_WORKERS", "500"))
MAX_STREAM_LAG_SECONDS: float   = float(os.environ.get("MAX_STREAM_LAG_SECONDS", "5.0"))
# NOTE: Set to false for live RTSP cameras. True is only for offline MP4 file playback.
# V2 fix: even when true, live rtsp:// sources ALWAYS use the buffer-drain
# path (see rtsp/stream_client.py) — this flag only affects file/non-rtsp
# sources, where it paces reads at the source FPS.
SIMULATE_REALTIME_PLAYBACK: bool = os.environ.get("SIMULATE_REALTIME_PLAYBACK", "false").lower() == "true"

# V4 fix: FFmpeg RTSP socket timeout (seconds). Applied to
# OPENCV_FFMPEG_CAPTURE_OPTIONS as `stimeout` (in MICROSECONDS) so a dead
# camera TCP connection can never block cap.grab() forever.
RTSP_SOCKET_TIMEOUT_SECONDS: float = float(os.environ.get("RTSP_SOCKET_TIMEOUT_SECONDS", "5.0"))

# Interval at which the service polls the Violation API for camera config changes.
# Any camera added/removed/reassigned in the dashboard takes effect within this window.
# Default follows CAMERA_POLL_INTERVAL_SECONDS unless explicitly overridden.
CONFIG_POLL_INTERVAL_SECONDS: int = int(
    os.environ.get("CONFIG_POLL_INTERVAL_SECONDS", str(CAMERA_POLL_INTERVAL_SECONDS))
)

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
# V7 fix: device selection override. Auto-detection prefers MPS on Apple
# Silicon, but variable-shaped person crops make the MPS allocator grow
# unbounded on some torch versions. Set FORCE_DEVICE=cpu|cuda|mps to pin the
# device explicitly. Empty string = auto-detect (legacy behaviour).
FORCE_DEVICE: str = os.environ.get("FORCE_DEVICE", "").strip().lower()
# When running on MPS, call torch.mps.empty_cache() every N frames so cached
# allocations for odd crop shapes are returned to the OS. <=0 disables.
MPS_EMPTY_CACHE_EVERY_N_FRAMES: int = int(os.environ.get("MPS_EMPTY_CACHE_EVERY_N_FRAMES", "50"))

# Max seconds the capture thread waits for the ViolationManager state decision
# before dropping the frame (protects the RTSP drain loop from a stalled
# event loop / violation manager).
FRAME_DECISION_TIMEOUT_SECONDS: float = float(os.environ.get("FRAME_DECISION_TIMEOUT_SECONDS", "5.0"))

MIN_CONFIDENCE_ROBOFLOW: float = float(os.environ.get("MIN_CONFIDENCE_ROBOFLOW", "0.60"))
MIN_CONFIDENCE_HUGGINGFACE: float = float(os.environ.get("MIN_CONFIDENCE_HUGGINGFACE", "0.40"))
# Restaurant PPE (mask / gloves / hairnet) — must meet this score or the
# detection is suppressed before any violation logic runs.
# M3 fix: default lowered from 0.65 to 0.55 to match production .env. The
# original docstring claimed "below 0.55 produces too many false positives";
# field-testing showed 0.55 is the actual sweet spot for wide-angle CCTV
# (the 0.65 was a paranoid initial pick during model rollout). Aligning so
# fresh deploys behave like staging.
MIN_CONFIDENCE_RESTAURANT_PPE: float = float(os.environ.get("MIN_CONFIDENCE_RESTAURANT_PPE", "0.80"))
MIN_CONFIDENCE_PEST: float           = float(os.environ.get("MIN_CONFIDENCE_PEST", "0.50"))

# H3 fix: pull these off the inference_engine literals (`conf=0.25`,
# `threshold=0.25`, `iou_threshold=0.45`) so operators can tune them via
# env without code edits. Names mirror the call sites for grep-ability.
# Generic ultralytics YOLO/YOLOWorld predict() confidence floor.
GENERIC_YOLO_MIN_CONFIDENCE: float    = float(os.environ.get("GENERIC_YOLO_MIN_CONFIDENCE", "0.25"))
# Legacy YOLOS-tiny HF pipeline threshold (used only if YOLO11n cannot load).
HF_LEGACY_DETECTION_THRESHOLD: float  = float(os.environ.get("HF_LEGACY_DETECTION_THRESHOLD", "0.25"))
# Cross-crop NMS IoU threshold for restaurant PPE deduping.
NMS_IOU_THRESHOLD: float              = float(os.environ.get("NMS_IOU_THRESHOLD", "0.45"))

# Identity-level dedupe for violation posting. If the same identified subject
# (employee or unknown vector id) triggers the same SOP on the same camera
# within this window, posting is suppressed.
IDENTITY_DEDUP_SECONDS: float = float(os.environ.get("IDENTITY_DEDUP_SECONDS", "240"))

# H10 fix: optional on-disk persistence for the violation DLQ. When set, the
# ViolationApiClient mirrors every queued payload to this SQLite file and
# re-hydrates on startup, so a service restart never silently drops a
# violation that was queued during a backend outage. Leave empty to disable
# (memory-only). The file is created on first use; choose a writable path
# (e.g. /var/lib/alpha-vision/dlq.sqlite or /tmp/vision-dlq.sqlite).
DLQ_PERSIST_PATH: str = os.environ.get("DLQ_PERSIST_PATH", "").strip()

# M21 fix: WHIP encoder parameters used by stream_client._start_ffmpeg.
# Pulling these out of code lets a deployment trade CPU vs quality without
# rebuilding the image. Defaults reproduce the legacy hard-coded values.
WHIP_VIDEO_CODEC: str   = os.environ.get("WHIP_VIDEO_CODEC", "libx264")
WHIP_VIDEO_PRESET: str  = os.environ.get("WHIP_VIDEO_PRESET", "ultrafast")
WHIP_VIDEO_TUNE: str    = os.environ.get("WHIP_VIDEO_TUNE", "zerolatency")
WHIP_VIDEO_PROFILE: str = os.environ.get("WHIP_VIDEO_PROFILE", "baseline")
WHIP_VIDEO_LEVEL: str   = os.environ.get("WHIP_VIDEO_LEVEL", "3.1")
WHIP_FRAMERATE: int     = int(os.environ.get("WHIP_FRAMERATE", "30"))
WHIP_GOP_SIZE: int      = int(os.environ.get("WHIP_GOP_SIZE", "30"))
WHIP_AUDIO_CODEC: str   = os.environ.get("WHIP_AUDIO_CODEC", "libopus")
WHIP_AUDIO_BITRATE: str = os.environ.get("WHIP_AUDIO_BITRATE", "128k")

# M19 fix: tracker thresholds were hard-coded inside SimpleIouTracker.
# Surface them so operators can tune association sensitivity per deployment
# (e.g. lower IoU for fast-moving subjects, longer missing window for
# cameras with frequent brief occlusion).
TRACKER_IOU_THRESHOLD: float       = float(os.environ.get("TRACKER_IOU_THRESHOLD", "0.3"))
TRACKER_MAX_MISSING_FRAMES: int    = int(os.environ.get("TRACKER_MAX_MISSING_FRAMES", "30"))

# M20 fix: image-pre-processing constants used by inference engine.
CLAHE_CLIP_LIMIT: float            = float(os.environ.get("CLAHE_CLIP_LIMIT", "2.0"))
CLAHE_TILE_GRID: int               = int(os.environ.get("CLAHE_TILE_GRID", "8"))
GAMMA_DARK_THRESHOLD: float        = float(os.environ.get("GAMMA_DARK_THRESHOLD", "80.0"))
GAMMA_DARK_VALUE: float            = float(os.environ.get("GAMMA_DARK_VALUE", "1.3"))

# M1/M13 fix: face recognizer settings used to be read directly from
# os.getenv inside inference/face_recognizer.py, which made them invisible
# to log_config() and impossible to override in tests via monkeypatch on
# the config module. Surface them here so config.py is the single source
# of truth. Defaults match the previous in-module fallbacks.
HUMAN_REID_URL: str                = (
    os.environ.get("HUMAN_REID_URL")
    or os.environ.get("Services__Reid__HttpUrl")
    or os.environ.get("Services__reid__http__0")
    # M13 fix: keep host.docker.internal as the local-dev default but
    # leave it overridable; production/Kubernetes must set HUMAN_REID_URL.
    or "http://host.docker.internal:8001"
)
HUMAN_REID_MATCH_THRESHOLD: float  = float(os.environ.get("HUMAN_REID_MATCH_THRESHOLD", "0.92"))
HUMAN_REID_KNOWN_MIN_MARGIN: float = float(os.environ.get("HUMAN_REID_KNOWN_MIN_MARGIN", "0.05"))
HUMAN_REID_TIMEOUT_SECONDS: float  = float(os.environ.get("HUMAN_REID_TIMEOUT_SECONDS", "3.0"))
UNKNOWN_REID_THRESHOLD: float      = float(os.environ.get("UNKNOWN_REID_THRESHOLD", "0.80"))
UNKNOWN_ID_PREFIX: str             = os.environ.get("UNKNOWN_ID_PREFIX", "unknown:")
ENABLE_UNKNOWN_REID_TRACKING: bool = os.environ.get("ENABLE_UNKNOWN_REID_TRACKING", "true").lower() == "true"
FACE_MIN_DIM_PX: int               = int(os.environ.get("FACE_MIN_DIM_PX", "60"))

# M10 fix: restaurant PPE label-mapping toggles, previously read inline.
RESTAURANT_PPE_PREFER_NO_MASK_LABEL: bool   = os.environ.get("RESTAURANT_PPE_PREFER_NO_MASK_LABEL", "true").lower() == "true"
RESTAURANT_PPE_ENABLE_OVERSIZE_FILTER: bool = os.environ.get("RESTAURANT_PPE_ENABLE_OVERSIZE_FILTER", "true").lower() == "true"
RESTAURANT_PPE_MAX_HEADBOX_AREA_RATIO: float = float(os.environ.get("RESTAURANT_PPE_MAX_HEADBOX_AREA_RATIO", "0.70"))

# M5 fix: data collector confidence thresholds, previously hard-coded
# inside DataCollector.collect_inference_event(). Surface so ops can
# widen/narrow the band without code change.
DATA_COLLECTOR_LOW_CONF_MIN: float  = float(os.environ.get("DATA_COLLECTOR_LOW_CONF_MIN", "0.20"))
DATA_COLLECTOR_LOW_CONF_MAX: float  = float(os.environ.get("DATA_COLLECTOR_LOW_CONF_MAX", "0.60"))

# ─── Startup Summary ─────────────────────────────────────────────────────────
def log_config(logger) -> None:
    """Print a startup config summary to the given logger."""
    mode_label = "⚠️  TESTING (AWS disabled)" if TESTING_MODE else "🚀 PRODUCTION (AWS enabled)"
    logger.info("=" * 60)
    logger.info("  Mode             : %s", mode_label)
    # V1 fix: log the raw flags so misconfigured .env/.env.local precedence
    # is visible at startup (SIMULATE_REALTIME_PLAYBACK=true on a live
    # deployment used to be silent and caused unbounded RTSP buffering).
    logger.info("  TESTING_MODE     : %s", TESTING_MODE)
    logger.info("  SIMULATE_REALTIME_PLAYBACK : %s", SIMULATE_REALTIME_PLAYBACK)
    logger.info("  Violation API    : %s", VIOLATION_API_BASE_URL)
    logger.info("  Target FPS       : %.1f", TARGET_FPS)
    logger.info("  Frame Timeout    : %.1fs", FRAME_TIMEOUT_SECONDS)
    logger.info("  Poll Interval    : %ds", CONFIG_POLL_INTERVAL_SECONDS)
    logger.info("  Identity Dedupe  : %.0fs", IDENTITY_DEDUP_SECONDS)
    logger.info("  Max Workers      : %d", MAX_STREAM_WORKERS)
    logger.info("  Device Tenant    : %s", DEVICE_TENANT_ID or "(none — single-device mode)")
    logger.info("  Device ID (env)  : %s", DEVICE_ID or "(auto — file/UUID)")
    logger.info("  Model Cache Dir  : %s", MODEL_CACHE_DIR)
    if not TESTING_MODE:
        logger.info("  S3 Bucket        : %s", S3_BUCKET_NAME or "NOT SET")
        logger.info("  SQS Queue        : %s", SQS_QUEUE_URL or "NOT SET")
        logger.info("  AWS Region       : %s", AWS_REGION or "NOT SET")
    logger.info("=" * 60)
