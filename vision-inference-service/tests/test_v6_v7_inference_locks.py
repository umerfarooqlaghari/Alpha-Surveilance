"""
V6 / V7 acceptance tests — inference thread-safety and device handling.

  V6 — shared YOLO model predict() calls are serialised by a per-model lock
       (InferenceEngine generic path, _detect_persons, PestDetector).
  V7 — FORCE_DEVICE override + periodic torch.mps.empty_cache() on MPS.
"""
from tests._stubs import install_stubs

install_stubs()

import threading  # noqa: E402
import time  # noqa: E402

import config  # noqa: E402
import inference.inference_engine as ie  # noqa: E402
from inference.pest_detector import PestDetector  # noqa: E402


class _ConcurrencyProbe:
    """Records the maximum number of concurrent entries into predict()."""

    def __init__(self, hold_seconds: float = 0.03):
        self.hold = hold_seconds
        self.current = 0
        self.max_concurrent = 0
        self.calls = 0
        self._mutex = threading.Lock()

    def _enter(self):
        with self._mutex:
            self.current += 1
            self.calls += 1
            self.max_concurrent = max(self.max_concurrent, self.current)
        time.sleep(self.hold)
        with self._mutex:
            self.current -= 1
        return []


class _ProbeYolo(_ConcurrencyProbe):
    def predict(self, *a, **k):
        return self._enter()

    def __call__(self, *a, **k):  # PestDetector invokes the model directly
        return self._enter()


def _bare_engine() -> ie.InferenceEngine:
    """InferenceEngine without __init__ (no model loading), with just the
    state the lock/device helpers need."""
    eng = object.__new__(ie.InferenceEngine)
    eng._predict_locks = {}
    eng._predict_locks_guard = threading.Lock()
    eng._registry = {}
    eng._motion_cache = {}
    eng._roboflow_map = {}
    eng._roboflow_client = None
    eng.device = "cpu"
    eng._mps_frames_since_cache_release = 0
    return eng


def _hammer(fn, n_threads=6):
    threads = [threading.Thread(target=fn) for _ in range(n_threads)]
    for t in threads:
        t.start()
    for t in threads:
        t.join(timeout=30)


# ─────────────────────────────────────────────────────────────────────────────
# V6 — per-model lock in the engine
# ─────────────────────────────────────────────────────────────────────────────

def test_predict_lock_is_per_model_instance():
    eng = _bare_engine()
    m1, m2 = object(), object()
    assert eng._predict_lock(m1) is eng._predict_lock(m1)  # stable
    assert eng._predict_lock(m1) is not eng._predict_lock(m2)  # per model


def test_detect_persons_never_runs_predict_concurrently(monkeypatch):
    eng = _bare_engine()
    probe = _ProbeYolo()
    eng._registry["human-detection-v1"] = probe
    monkeypatch.setattr(ie, "HAS_ULTRALYTICS", True)
    monkeypatch.setattr(ie, "YOLO", _ProbeYolo)
    monkeypatch.setattr(ie, "YOLOWorld", _ProbeYolo)

    _hammer(lambda: eng._detect_persons(object()))

    assert probe.calls == 6
    assert probe.max_concurrent == 1, (
        f"predict() entered concurrently ({probe.max_concurrent} threads) — lock missing"
    )


def test_generic_yolo_path_never_runs_predict_concurrently(monkeypatch):
    eng = _bare_engine()
    probe = _ProbeYolo()
    eng._registry["some-yolo-model"] = probe
    monkeypatch.setattr(ie, "HAS_ULTRALYTICS", True)
    monkeypatch.setattr(ie, "YOLO", _ProbeYolo)
    monkeypatch.setattr(ie, "YOLOWorld", _ProbeYolo)
    # Keep the person-crop/motion-gate machinery out of the generic path
    monkeypatch.setattr(ie.config, "MOTION_GATE_ENABLED", False)

    class _Rule:
        model_identifier = "some-yolo-model"
        model_status = "Available"
        model_type = "YoloLocal"
        trigger_labels = ["person"]
        model_min_confidence = None
        model_image_size = None

    _hammer(lambda: eng.run_inference(object(), [_Rule()], camera_id="CAM-1"))

    assert probe.calls == 6
    assert probe.max_concurrent == 1


def test_pest_detector_predict_is_serialised(tmp_path):
    weights = tmp_path / "pest.pt"
    weights.write_bytes(b"fake-weights")
    probe = _ProbeYolo()
    det = PestDetector(
        weights_path=str(weights),
        yolo_cls=lambda p: probe,
        device="cpu",
    )
    assert det.available

    _hammer(lambda: det.predict(object()))

    assert probe.calls == 6
    assert probe.max_concurrent == 1


# ─────────────────────────────────────────────────────────────────────────────
# V7 — FORCE_DEVICE + MPS cache release
# ─────────────────────────────────────────────────────────────────────────────

def test_mps_cache_release_counts_frames(monkeypatch):
    eng = _bare_engine()
    eng.device = "mps"
    monkeypatch.setattr(config, "MPS_EMPTY_CACHE_EVERY_N_FRAMES", 3)
    released = []
    monkeypatch.setattr(ie.torch.mps, "empty_cache", lambda: released.append(1))

    for _ in range(7):
        eng._maybe_release_mps_cache()

    assert len(released) == 2  # frames 3 and 6


def test_mps_cache_release_disabled_when_nonpositive(monkeypatch):
    eng = _bare_engine()
    eng.device = "mps"
    monkeypatch.setattr(config, "MPS_EMPTY_CACHE_EVERY_N_FRAMES", 0)
    released = []
    monkeypatch.setattr(ie.torch.mps, "empty_cache", lambda: released.append(1))

    for _ in range(10):
        eng._maybe_release_mps_cache()
    assert released == []


def test_mps_cache_release_noop_on_cpu(monkeypatch):
    eng = _bare_engine()
    eng.device = "cpu"
    monkeypatch.setattr(config, "MPS_EMPTY_CACHE_EVERY_N_FRAMES", 1)
    released = []
    monkeypatch.setattr(ie.torch.mps, "empty_cache", lambda: released.append(1))

    eng._maybe_release_mps_cache()
    assert released == []


def test_force_device_cpu_overrides_autodetect(monkeypatch):
    """FORCE_DEVICE=cpu must win even when MPS reports available."""
    monkeypatch.setattr(ie.torch.backends.mps, "is_available", lambda: True)
    monkeypatch.setattr(config, "FORCE_DEVICE", "cpu")
    monkeypatch.setattr(ie.InferenceEngine, "_load_models", lambda self: None)

    eng = ie.InferenceEngine()
    assert eng.device == "cpu"


def test_force_device_falls_back_when_unavailable(monkeypatch):
    monkeypatch.setattr(ie.torch.backends.mps, "is_available", lambda: False)
    monkeypatch.setattr(ie.torch.cuda, "is_available", lambda: False)
    monkeypatch.setattr(config, "FORCE_DEVICE", "cuda")
    monkeypatch.setattr(ie.InferenceEngine, "_load_models", lambda self: None)

    eng = ie.InferenceEngine()
    assert eng.device == "cpu"
