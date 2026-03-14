import pytest
import os
import sys

# Ensure the rtsp folder is discoverable during tests
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), '..')))
