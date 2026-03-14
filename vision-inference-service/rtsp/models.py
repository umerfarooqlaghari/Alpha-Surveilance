"""
rtsp/models.py
Data models for camera configuration and stream state tracking.
"""
from dataclasses import dataclass, field
from datetime import datetime
from typing import Optional, Literal

StreamStatus = Literal["idle", "connecting", "running", "reconnecting", "stopped", "error"]


@dataclass
class ViolationRule:
    """
    Granular rule for identifying a specific violation type.
    """
    sop_violation_type_id: str
    model_identifier: str
    trigger_labels: list[str] = field(default_factory=list)


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

    def __hash__(self):
        return hash(self.camera_id)

    def __eq__(self, other):
        return isinstance(other, CameraConfig) and self.camera_id == other.camera_id


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
    max_reconnect_attempts: int = 10        # After this many failures, stream enters "error" state
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
