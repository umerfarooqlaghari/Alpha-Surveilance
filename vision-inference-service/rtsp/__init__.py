"""
rtsp/__init__.py
Public interface for the RTSP stream management package.
"""
from .models import CameraConfig, StreamState, StreamStatus


def __getattr__(name):
    if name == "CameraStreamManager":
        from .stream_manager import CameraStreamManager

        return CameraStreamManager
    if name == "ViolationApiClient":
        from .violation_api_client import ViolationApiClient

        return ViolationApiClient
    raise AttributeError(name)

__all__ = [
    "CameraConfig",
    "StreamState",
    "StreamStatus",
    "CameraStreamManager",
    "ViolationApiClient",
]
