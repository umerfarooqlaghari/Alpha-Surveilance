"""
metrics.py
Prometheus metric surface for the vision-inference service.

Audit P4 #17: the original service exposed only logs, so questions like
"what's the p95 inference latency per camera?" or "how many violations got
dropped due to API errors?" required grepping JSON logs. This module
centralises every operationally-interesting counter/histogram so a single
``/metrics`` endpoint surfaces them in Prometheus format.

Metric naming follows Prometheus conventions:
  - ``vision_*`` namespace
  - ``_total`` suffix on counters
  - ``_seconds`` suffix on duration histograms
  - low-cardinality labels only (``camera_id`` and ``model_id`` are bounded)

Importing this module is cheap — it just declares the metric objects. The
``/metrics`` route wired in main.py serves them in text-exposition format.
"""
from __future__ import annotations

from prometheus_client import Counter, Gauge, Histogram, CollectorRegistry, generate_latest, CONTENT_TYPE_LATEST


# A dedicated registry keeps test runs isolated from the default global one
# (multiple imports during pytest would otherwise raise "Duplicated timeseries").
REGISTRY = CollectorRegistry()


# ─── Inference pipeline ──────────────────────────────────────────────────────
inference_latency_seconds = Histogram(
    "vision_inference_latency_seconds",
    "Wall-clock duration of run_inference() per frame.",
    labelnames=("camera_id",),
    buckets=(0.05, 0.1, 0.25, 0.5, 1.0, 2.0, 5.0, 10.0, 30.0),
    registry=REGISTRY,
)
frames_processed_total = Counter(
    "vision_frames_processed_total",
    "Frames fed to the inference pipeline (whether or not they produced a detection).",
    labelnames=("camera_id",),
    registry=REGISTRY,
)
detections_total = Counter(
    "vision_detections_total",
    "Raw detections emitted by the model layer (pre-rule-evaluation).",
    labelnames=("camera_id", "model_id"),
    registry=REGISTRY,
)
violations_emitted_total = Counter(
    "vision_violations_emitted_total",
    "Violations that passed all rule filters and were queued for API delivery.",
    labelnames=("camera_id", "model_id"),
    registry=REGISTRY,
)


# ─── Person pre-layer ────────────────────────────────────────────────────────
person_detector_runs_total = Counter(
    "vision_person_detector_runs_total",
    "Times YOLOv11n person detection actually ran (i.e. wasn't motion-gated).",
    registry=REGISTRY,
)
motion_gate_hits_total = Counter(
    "vision_motion_gate_hits_total",
    "Times the motion gate reused the previous frame's person boxes instead of re-running YOLOv11n.",
    registry=REGISTRY,
)
person_fallback_total = Counter(
    "vision_person_fallback_total",
    "Times PPE inference fell back to full-frame because no persons were detected.",
    labelnames=("camera_id",),
    registry=REGISTRY,
)


# ─── API delivery ────────────────────────────────────────────────────────────
api_post_total = Counter(
    "vision_violation_api_post_total",
    "Attempts to POST a violation to the Violation Management API.",
    labelnames=("outcome",),  # success | transient_fail | permanent_fail
    registry=REGISTRY,
)
api_dlq_size = Gauge(
    "vision_violation_api_dlq_size",
    "Current size of the in-memory dead-letter queue for post_violation.",
    registry=REGISTRY,
)


# ─── Helper for the FastAPI route ────────────────────────────────────────────

def render_text() -> tuple:
    """Return (body, content_type) tuple for serving the /metrics endpoint."""
    return generate_latest(REGISTRY), CONTENT_TYPE_LATEST
