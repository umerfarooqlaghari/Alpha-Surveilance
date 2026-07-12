"""
tests/_stubs.py
===============
sys.modules stubs for heavy native dependencies (torch / cv2 / ultralytics /
transformers / boto3) so the crash/leak-fix test suite can import config.py,
main.py, rtsp/* and inference/* without installing GPU or OpenCV stacks.

Call ``install_stubs()`` at the very top of a test module, BEFORE importing
any service module. Idempotent; never replaces a module that is already
importable/imported for real.
"""
import importlib.util
import os
import sys
import types


def _numpy():
    import numpy as np
    return np


def _missing(module_name: str) -> bool:
    """True when the module is neither imported nor installed for real."""
    if module_name in sys.modules:
        return False
    try:
        return importlib.util.find_spec(module_name) is None
    except (ImportError, ValueError):
        return True


def install_stubs() -> None:
    # config.py must see testing mode before first import, otherwise it raises
    # on missing INTERNAL_API_KEY / MODEL_S3_BUCKET. Real process env wins over
    # .env/.env.local, so this also isolates tests from repo dotenv files.
    os.environ["TESTING_MODE"] = "true"
    os.environ.setdefault("LOG_LEVEL", "INFO")

    # Let rtsp/stream_client set its own OPENCV_FFMPEG_CAPTURE_OPTIONS default
    # (it uses setdefault at import time; a leftover value would mask the V4
    # stimeout assertion).
    if "rtsp.stream_client" not in sys.modules:
        os.environ.pop("OPENCV_FFMPEG_CAPTURE_OPTIONS", None)

    # ── torch ────────────────────────────────────────────────────────────
    if _missing("torch"):
        torch = types.ModuleType("torch")
        torch.backends = types.SimpleNamespace(
            mps=types.SimpleNamespace(is_available=lambda: False)
        )
        torch.cuda = types.SimpleNamespace(is_available=lambda: False)
        torch.mps = types.SimpleNamespace(empty_cache=lambda: None)
        sys.modules["torch"] = torch

    # ── cv2 ──────────────────────────────────────────────────────────────
    if _missing("cv2"):
        cv2 = types.ModuleType("cv2")
        cv2.CAP_FFMPEG = 1900
        cv2.CAP_PROP_BUFFERSIZE = 38
        cv2.CAP_PROP_FPS = 5
        cv2.COLOR_BGR2RGB = 4
        cv2.COLOR_RGB2BGR = 3
        cv2.COLOR_BGR2GRAY = 6
        cv2.FONT_HERSHEY_SIMPLEX = 0
        cv2.INTER_AREA = 3
        cv2.cvtColor = lambda img, code: img
        cv2.resize = lambda img, size, interpolation=None: img
        cv2.rectangle = lambda *a, **k: None
        cv2.putText = lambda *a, **k: None
        cv2.imencode = lambda ext, img: (True, _numpy().zeros(1, dtype="uint8"))
        cv2.createCLAHE = lambda **k: types.SimpleNamespace(apply=lambda x: x)

        class _ClosedCap:
            """Default VideoCapture stub — tests replace it as needed."""

            def __init__(self, *a, **k):
                pass

            def isOpened(self):
                return False

            def release(self):
                pass

            def set(self, *a):
                pass

            def get(self, *a):
                return 0.0

            def grab(self):
                return False

            def retrieve(self):
                return False, None

            def read(self):
                return False, None

        cv2.VideoCapture = _ClosedCap
        sys.modules["cv2"] = cv2

    # ── transformers ─────────────────────────────────────────────────────
    if _missing("transformers"):
        transformers = types.ModuleType("transformers")
        transformers.pipeline = lambda *a, **k: (lambda *aa, **kk: [])
        sys.modules["transformers"] = transformers

    # ── boto3 / botocore ─────────────────────────────────────────────────
    if _missing("boto3"):
        boto3 = types.ModuleType("boto3")
        boto3.client = lambda *a, **k: types.SimpleNamespace(
            put_object=lambda **kw: None,
            download_file=lambda *aa, **kk: None,
        )
        sys.modules["boto3"] = boto3
    if _missing("botocore"):
        botocore = types.ModuleType("botocore")
        bc_config = types.ModuleType("botocore.config")

        class _BotoCfg:
            def __init__(self, *a, **k):
                self.kwargs = k

        bc_config.Config = _BotoCfg
        bc_exc = types.ModuleType("botocore.exceptions")

        class ClientError(Exception):
            pass

        class NoCredentialsError(Exception):
            pass

        bc_exc.ClientError = ClientError
        bc_exc.NoCredentialsError = NoCredentialsError
        botocore.config = bc_config
        botocore.exceptions = bc_exc
        sys.modules["botocore"] = botocore
        sys.modules["botocore.config"] = bc_config
        sys.modules["botocore.exceptions"] = bc_exc

    # ── shapely may not be installed in minimal CI (rules/spatial needs it) ──
    try:
        import shapely  # noqa: F401
    except ImportError:  # pragma: no cover
        shp = types.ModuleType("shapely")
        geom = types.ModuleType("shapely.geometry")

        class _Geom:
            def __init__(self, *a, **k):
                pass

            def contains(self, other):
                return False

            @property
            def is_valid(self):
                return True

        geom.Point = _Geom
        geom.Polygon = _Geom
        validation = types.ModuleType("shapely.validation")
        validation.make_valid = lambda g: g
        shp.geometry = geom
        shp.validation = validation
        sys.modules["shapely"] = shp
        sys.modules["shapely.geometry"] = geom
        sys.modules["shapely.validation"] = validation
