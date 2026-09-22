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
    if os.environ.get("FORCE_STUBS", "").lower() == "true":
        return True
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
    os.environ.setdefault("FORCE_STUBS", "true")

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
        cv2.resize = lambda img, size, interpolation=None: _numpy().zeros((size[1], size[0], img.shape[2]) if getattr(img, 'ndim', 0) > 2 else (size[1], size[0]), dtype=getattr(img, 'dtype', 'uint8'))
        cv2.rectangle = lambda *a, **k: None
        cv2.putText = lambda *a, **k: None
        cv2.getTextSize = lambda text, font, font_scale, thickness: ((int(len(str(text)) * 10 * font_scale), int(20 * font_scale)), 5)
        cv2.LINE_AA = 16
        # video_annotator draws a translucent HUD bar; addWeighted blends the
        # overlay copy back into the frame in place.
        cv2.addWeighted = lambda src1, alpha, src2, beta, gamma, dst=None: (
            dst if dst is not None else src1
        )
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

    # ── httpx ────────────────────────────────────────────────────────────
    if _missing("httpx"):
        httpx = types.ModuleType("httpx")

        class _Client:
            def __init__(self, *a, **k):
                self.base_url = k.get("base_url", "")
                self.headers = k.get("headers", {})

            def get(self, *a, **k):
                return types.SimpleNamespace(status_code=200, json=lambda: [], raise_for_status=lambda: None)

            def post(self, *a, **k):
                return types.SimpleNamespace(status_code=200, json=lambda: {}, raise_for_status=lambda: None)

        httpx.Client = _Client
        httpx.AsyncClient = _Client
        httpx.Timeout = lambda *a, **k: None
        httpx.Limits = lambda *a, **k: None
        httpx.HTTPError = Exception
        httpx.RequestError = Exception
        httpx.TimeoutException = Exception
        sys.modules["httpx"] = httpx

    # ── prometheus_client ────────────────────────────────────────────────
    # metrics.py is imported by main.py and rtsp/clip_recorder.py; the real
    # package is a runtime dependency (requirements.txt) but tests must import
    # the service without it, like every other heavy dep stubbed here.
    if _missing("prometheus_client"):
        prom = types.ModuleType("prometheus_client")

        class _Metric:
            def __init__(self, *a, **k):
                self._labelnames = k.get("labelnames", ())

            def labels(self, *a, **k):
                return self

            def inc(self, *a, **k):
                return None

            def dec(self, *a, **k):
                return None

            def set(self, *a, **k):
                return None

            def observe(self, *a, **k):
                return None

            def time(self):
                class _Ctx:
                    def __enter__(self_inner):
                        return self_inner

                    def __exit__(self_inner, *exc):
                        return False

                return _Ctx()

        prom.Counter = _Metric
        prom.Gauge = _Metric
        prom.Histogram = _Metric
        prom.Summary = _Metric
        prom.CollectorRegistry = lambda *a, **k: object()
        prom.generate_latest = lambda *a, **k: b""
        prom.CONTENT_TYPE_LATEST = "text/plain; version=0.0.4; charset=utf-8"
        sys.modules["prometheus_client"] = prom

    # ── scipy ────────────────────────────────────────────────────────────
    if _missing("scipy"):
        scipy = types.ModuleType("scipy")
        scipy_opt = types.ModuleType("scipy.optimize")
        scipy_opt.linear_sum_assignment = lambda cost_matrix: (_numpy().array([]), _numpy().array([]))
        scipy.optimize = scipy_opt
        sys.modules["scipy"] = scipy
        sys.modules["scipy.optimize"] = scipy_opt
