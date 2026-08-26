"""
rtsp/models.py
Data models for camera configuration and stream state tracking.
"""
from dataclasses import dataclass, field
from datetime import datetime, time as dt_time
from typing import Optional, Literal

StreamStatus = Literal["idle", "connecting", "running", "reconnecting", "stopped", "error"]


@dataclass
class DetectionScheduleItem:
    """
    A recurring UTC sleep window.  When the current UTC time falls inside
    any active DetectionScheduleItem the Vision Service skips inference for
    that camera (frames are still captured to keep the stream alive).

    DaysOfWeek bitmask mirrors .NET DayOfWeek:
      Sunday=1, Monday=2, Tuesday=4, Wednesday=8,
      Thursday=16, Friday=32, Saturday=64.
    0 or 127 = every day.
    """
    start_time: str        # "HH:mm" UTC
    end_time: str          # "HH:mm" UTC (may be < start_time for overnight windows)
    days_of_week: int = 127  # 0 or 127 = all days
    is_active: bool = True
    label: str = ""


@dataclass
class ViolationRule:
    """
    Granular rule for identifying a specific violation type.
    """
    sop_violation_type_id: str
    model_identifier: str
    trigger_labels: list[str] = field(default_factory=list)
    rule_config: dict = field(default_factory=dict)

    # ── AiModel registry fields (populated from camera config API) ────────────
    model_status: str = "Available"       # "Available" | "Disabled" | "Registered" | "Error"
    model_type: str   = "YoloLocal"       # "YoloLocal" | "YoloFineTuned" | "RoboflowCloud" | "OpenVocabGrounding"
    model_download_url: str | None = None
    model_s3_bucket: str | None = None
    model_s3_key: str | None = None
    model_min_confidence: float | None = None
    model_image_size: int | None = None
    model_requires_cropping: bool = False
    model_requires_human_presence: bool = False
    model_local_path: str | None = None
    model_sha256: str | None = None
    ai_model_id: str | None = None        # DB Guid of the AiModel record

    def __post_init__(self):
        # D-6 fix: normalise trigger_labels to lowercase stripped strings at
        # construction time so comparisons in the evaluator and ViolationManager
        # are always case-consistent, regardless of what the API returns.
        # e.g. "No-Glove" → "no-glove", "  Missing Mask  " → "missing mask"
        self.trigger_labels = [
            str(lbl).strip().lower()
            for lbl in self.trigger_labels
            if str(lbl).strip()
        ]


@dataclass
class CameraConfig:
    """
    Immutable configuration for a single camera stream.
    Populated from the Violation API /api/cameras/internal/active response.
    """
    camera_db_id: str       # UUID from Cameras table (used when posting violations)
    camera_id: str          # Friendly slug e.g. "CAM-GATE-01"
    tenant_id: str          # Tenant UUID
    tenant_name: str        # Tenant Name
    rtsp_url: str           # Decrypted RTSP stream URL
    whip_url: str = ""      # Cloudflare WHIP Publisher URL
    is_streaming: bool = False # Whether the camera is actively marked as streaming
    name: str = ""
    location: str = ""

    # Specific rules (model + labels) to run on this stream
    violation_rules: list[ViolationRule] = field(default_factory=list)

    # Stream tuning
    target_fps: float = 1.0             # Frames per second to process (not capture)
    frame_timeout_seconds: float = 30.0 # Watchdog triggers reconnect if no frame

    # Detection kill-switch — when False the Vision Service opens no RTSP
    # connection for this camera (no decode, no inference, no violations).
    is_detection_enabled: bool = True

    # Recurring sleep windows: Vision Service skips inference when inside any
    # active window.  Frames are still decoded to keep the RTSP stream alive.
    detection_schedules: list[DetectionScheduleItem] = field(default_factory=list)

    # Attendance marking configuration: "None", "MarkIn", "MarkOut", "Bidirectional"
    attendance_mode: str = "None"

    def __hash__(self):
        return hash(self.camera_id)

    def __eq__(self, other):
        # L6 fix: previously this only compared `camera_id`, which meant two
        # CameraConfig instances with the same id but *different* rtsp_url /
        # rules / schedules were treated as equal. That hides legitimate
        # changes from any naive `if old_cam == new_cam` reconcile path.
        # Hot-reload still uses `_camera_signature()` (in main.py) for
        # difference detection, so this fix is defence in depth.
        if not isinstance(other, CameraConfig):
            return NotImplemented
        return (
            self.camera_id == other.camera_id
            and self.camera_db_id == other.camera_db_id
            and self.tenant_id == other.tenant_id
            and self.rtsp_url == other.rtsp_url
            and self.whip_url == other.whip_url
            and self.is_streaming == other.is_streaming
            and self.is_detection_enabled == other.is_detection_enabled
            and getattr(self, "attendance_mode", "None") == getattr(other, "attendance_mode", "None")
            and round(self.target_fps, 3) == round(other.target_fps, 3)
            and self.violation_rules == other.violation_rules
            and self.detection_schedules == other.detection_schedules
        )


@dataclass
class StreamState:
    """
    Mutable runtime state for a single camera stream.
    Thread-safe reads are caller's responsibility (managed by RtspStreamClient).
    """
    camera_id: str
    camera_db_id: str
    tenant_id: str
    name: str
    location: str
    status: StreamStatus = "idle"

    reconnect_attempts: int = 0
    # V3 fix: <= 0 means "retry forever" (exponential backoff capped at
    # RtspStreamClient.RECONNECT_MAX_DELAY). This is now the default: a
    # finite cap permanently killed the capture loop after ~10 minutes of
    # camera downtime, and nothing restarted it until the next config CHANGE.
    # Set a positive value to restore the legacy give-up-after-N behaviour.
    max_reconnect_attempts: int = 0
    frames_processed: int = 0
    frames_skipped: int = 0
    frames_ghost: int = 0                   # Black/blank frames — indicates a ghost connection

    started_at: Optional[datetime] = None
    last_frame_at: Optional[datetime] = None
    last_error: Optional[str] = None
    last_reconnect_at: Optional[datetime] = None

    def to_dict(self) -> dict:
        return {
            "camera_id": self.camera_id,
            "camera_db_id": self.camera_db_id,
            "tenant_id": self.tenant_id,
            "name": self.name,
            "location": self.location,
            "status": self.status,
            "reconnect_attempts": self.reconnect_attempts,
            "frames_processed": self.frames_processed,
            "frames_skipped": self.frames_skipped,
            "frames_ghost": self.frames_ghost,
            "healthy": self.frames_processed > 0 and self.frames_ghost == 0,
            "started_at": self.started_at.isoformat() if self.started_at else None,
            "last_frame_at": self.last_frame_at.isoformat() if self.last_frame_at else None,
            "last_error": self.last_error,
            "last_reconnect_at": self.last_reconnect_at.isoformat() if self.last_reconnect_at else None,
        }
