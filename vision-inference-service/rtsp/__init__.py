"""
rtsp/__init__.py
Public interface for the RTSP stream management package.
"""
from .models import CameraConfig, StreamState, StreamStatus
from .stream_manager import CameraStreamManager
from .violation_api_client import ViolationApiClient

__all__ = [
    "CameraConfig",
    "StreamState",
    "StreamStatus",
    "CameraStreamManager",
    "ViolationApiClient",
]
