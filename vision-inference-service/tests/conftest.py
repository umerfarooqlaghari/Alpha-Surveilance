import pytest
import os
import sys

# Ensure the rtsp folder is discoverable during tests
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), '..')))

# Install lightweight sys.modules stubs for heavy native deps (torch, cv2,
# ultralytics, transformers, boto3) so the suite can run on machines without
# the GPU/OpenCV stacks. Strictly a no-op for any dependency that is actually
# installed — real modules always win (see tests/_stubs.py).
from tests._stubs import install_stubs  # noqa: E402

install_stubs()
