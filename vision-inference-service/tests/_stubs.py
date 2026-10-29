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
        cv2.COLOR_RGB2GRAY = 7
        cv2.COLOR_BGRA2GRAY = 10
        cv2.COLOR_RGB2HSV = 40
        cv2.COLOR_RGB2LAB = 44
        cv2.COLOR_BGR2LAB = 45
        cv2.COLOR_LAB2BGR = 46
        cv2.FONT_HERSHEY_SIMPLEX = 0
        cv2.INTER_AREA = 3
        cv2.INTER_LINEAR = 1
        cv2.CV_32F = 5
        cv2.cvtColor = lambda img, code: (
            _numpy().zeros((img.shape[0], img.shape[1]), dtype=getattr(img, 'dtype', 'uint8'))
            if code in (6, 7, 10)
            else (img[:, :, ::-1].copy() if code in (3, 4) and getattr(img, 'ndim', 0) == 3 else (img.copy() if getattr(img, 'ndim', 0) == 3 else img))
        )
        cv2.split = lambda img: [img[:, :, i] for i in range(img.shape[2])] if getattr(img, 'ndim', 0) == 3 else [img]
        cv2.merge = lambda channels: _numpy().stack(channels, axis=2)
        cv2.LUT = lambda src, lut: src
        cv2.resize = lambda img, size, interpolation=None: _numpy().zeros((size[1], size[0], img.shape[2]) if getattr(img, 'ndim', 0) > 2 else (size[1], size[0]), dtype=getattr(img, 'dtype', 'uint8'))
        cv2.calcHist = lambda images, channels, mask, histSize, ranges: _numpy().histogram(images[0][:, :, channels[0]], bins=histSize[0], range=(ranges[0], ranges[1]))[0].reshape(-1, 1).astype(_numpy().float32)
        cv2.Sobel = lambda src, ddepth, dx, dy, ksize=3: _numpy().ones_like(src, dtype=_numpy().float32)
        cv2.magnitude = lambda x, y: _numpy().sqrt(_numpy().square(x) + _numpy().square(y))
        cv2.rectangle = lambda *a, **k: None
        cv2.line = lambda *a, **k: None
        cv2.circle = lambda *a, **k: None
        cv2.putText = lambda *a, **k: None
        cv2.getTextSize = lambda text, font, font_scale, thickness: ((int(len(str(text)) * 10 * font_scale), int(20 * font_scale)), 5)
        cv2.LINE_AA = 16
        # video_annotator draws a translucent HUD bar; addWeighted blends the
        # overlay copy back into the frame in place.
        cv2.addWeighted = lambda src1, alpha, src2, beta, gamma, dst=None: (
            dst if dst is not None else src1
        )
        cv2.imencode = lambda ext, img, *a, **k: (True, _numpy().zeros(1, dtype="uint8"))
        cv2.imwrite = lambda path, img, *a, **k: True
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

        class _Point:
            def __init__(self, x=0, y=0, *a, **k):
                if isinstance(x, (tuple, list)):
                    self.x = float(x[0])
                    self.y = float(x[1]) if len(x) > 1 else 0.0
                else:
                    self.x = float(x)
                    self.y = float(y)
                self.geom_type = "Point"

            @property
            def is_valid(self):
                return True

            @property
            def is_empty(self):
                return False

        class _Polygon:
            def __init__(self, coords=None, *a, **k):
                if coords and hasattr(coords, "__iter__"):
                    self.coords = list(coords)
                else:
                    self.coords = []
                self.geom_type = "Polygon"

            def contains(self, other):
                if not hasattr(other, "x") or not hasattr(other, "y"):
                    return False
                px, py = other.x, other.y
                poly = self.coords
                if not poly or len(poly) < 3:
                    return False
                n = len(poly)
                inside = False
                p1x, p1y = poly[0][0], poly[0][1]
                for i in range(1, n + 1):
                    p2x, p2y = poly[i % n][0], poly[i % n][1]
                    if py > min(p1y, p2y):
                        if py <= max(p1y, p2y):
                            if px <= max(p1x, p2x):
                                if p1y != p2y:
                                    xinters = (py - p1y) * (p2x - p1x) / (p2y - p1y) + p1x
                                    if p1x == p2x or px <= xinters:
                                        inside = not inside
                                elif p1x == p2x:
                                    inside = not inside
                    p1x, p1y = p2x, p2y
                return inside

            def intersects(self, other):
                return self.contains(other)

            @property
            def is_valid(self):
                return True

            @property
            def is_empty(self):
                return len(self.coords) < 3

            @property
            def area(self):
                return 100.0 if len(self.coords) >= 3 else 0.0

        geom.Point = _Point
        geom.Polygon = _Polygon
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

        class HTTPStatusError(Exception):
            def __init__(self, message="", *, request=None, response=None):
                super().__init__(message)
                self.request = request
                self.response = response

        class Response:
            def __init__(self, status_code=200, json=None, request=None, text="", content=b""):
                self.status_code = status_code
                self._json = json
                self.request = request
                self.text = text
                self.content = content
            def json(self):
                return self._json
            def raise_for_status(self):
                if 400 <= self.status_code < 600:
                    raise HTTPStatusError(f"HTTP {self.status_code}", response=self, request=self.request)

        class Request:
            def __init__(self, method="GET", url=""):
                self.method = method
                self.url = url

        class _Client:
            def __init__(self, *a, **k):
                self.base_url = k.get("base_url", "")
                self.headers = k.get("headers", {})

            def get(self, *a, **k):
                return Response(status_code=200, json=[])

            async def get(self, *a, **k):
                return Response(status_code=200, json=[])

            def post(self, *a, **k):
                return Response(status_code=200, json={})

            async def post(self, *a, **k):
                return Response(status_code=200, json={})

            async def aclose(self):
                pass

        httpx.Client = _Client
        httpx.AsyncClient = _Client
        httpx.HTTPStatusError = HTTPStatusError
        httpx.Response = Response
        httpx.Request = Request
        httpx.Timeout = lambda *a, **k: None
        httpx.Limits = lambda *a, **k: None
        httpx.HTTPError = Exception
        httpx.RequestError = Exception
        httpx.ConnectError = ConnectionError
        httpx.TimeoutException = TimeoutError
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
                self._val = 0.0
                self._value = types.SimpleNamespace(get=lambda: self._val)
                self._children = {}

            def labels(self, *a, **k):
                key = (tuple(a), tuple(sorted(k.items())))
                if key not in self._children:
                    child = _Metric(*a, **k)
                    self._children[key] = child
                return self._children[key]

            def inc(self, amount=1, *a, **k):
                self._val += float(amount)
                return None

            def dec(self, amount=1, *a, **k):
                self._val -= float(amount)
                return None

            def set(self, val, *a, **k):
                self._val = float(val)
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



    # ── redis ────────────────────────────────────────────────────────────
    if _missing("redis"):
        redis_mod = types.ModuleType("redis")
        class _Redis:
            def __init__(self, *a, **k): pass
            def ping(self): return True
        redis_mod.Redis = _Redis
        redis_mod.ConnectionError = ConnectionError
        redis_mod.TimeoutError = TimeoutError
        sys.modules["redis"] = redis_mod

    # ── multipart ────────────────────────────────────────────────────────
    if _missing("multipart"):
        multipart_mod = types.ModuleType("multipart")
        multipart_mod.__version__ = "0.0.9"
        sys.modules["multipart"] = multipart_mod
    if _missing("python_multipart"):
        pm_mod = types.ModuleType("python_multipart")
        pm_mod.__version__ = "0.0.9"
        sys.modules["python_multipart"] = pm_mod

    # ── tensorrt ─────────────────────────────────────────────────────────
    if _missing("tensorrt"):
        trt_mod = types.ModuleType("tensorrt")
        class IInt8EntropyCalibrator2:
            def __init__(self, *a, **k): pass
            def get_batch_size(self): return 8
            def get_batch(self, names=None): return None
            def read_calibration_cache(self): return None
            def write_calibration_cache(self, cache): pass
        class Logger:
            INFO = 1
            WARNING = 2
            ERROR = 3
            def __init__(self, *a, **k): pass
        trt_mod.IInt8EntropyCalibrator2 = IInt8EntropyCalibrator2
        trt_mod.Logger = Logger
        trt_mod.__version__ = "10.0.1"
        sys.modules["tensorrt"] = trt_mod

