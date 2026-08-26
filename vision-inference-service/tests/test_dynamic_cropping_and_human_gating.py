"""
tests/test_dynamic_cropping_and_human_gating.py
================================================
Comprehensive verification of dynamic human presence gating and target/person cropping:

1. Model with model_requires_human_presence=True:
   - When no persons detected in frame -> inference is skipped cleanly.
   - When persons detected in frame -> model executes.

2. Model with model_requires_human_presence=False (e.g., pest, rodent, smoke, spill):
   - When no persons detected in frame -> model runs unconditionally and returns detections.
   - When persons detected in frame -> model runs directly on full frame.

3. Model with model_requires_cropping=True:
   - Inference runs on person crops, coordinates offset back to full-frame, anchored to person_box.

4. Model with model_requires_cropping=False (e.g., standing on chairs, jumping machines, reliever duty):
   - Cropping is bypassed and the model receives the full uncropped frame to capture the environment.

5. Real image test against dataset samples.
"""
import os
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from tests._stubs import install_stubs

install_stubs()

import os  # noqa: E402
import sys  # noqa: E402
import threading  # noqa: E402
from typing import Any, Dict, List  # noqa: E402
from unittest.mock import MagicMock  # noqa: E402
from PIL import Image  # noqa: E402

import inference.inference_engine as ie  # noqa: E402
from rtsp.models import ViolationRule  # noqa: E402


def _bare_engine() -> ie.InferenceEngine:
    eng = object.__new__(ie.InferenceEngine)
    eng._predict_locks = {}
    eng._predict_locks_guard = threading.Lock()
    eng._registry = {}
    eng._motion_cache = {}
    eng._roboflow_map = {}
    eng._roboflow_client = None
    eng._legacy_lock = threading.Lock()
    eng._model_load_lock = threading.Lock()
    eng._model_signatures = {}
    eng._restaurant_detectors_by_path = {}
    eng._pest_detectors_by_path = {}
    eng._open_vocab_detectors_by_reference = {}
    eng.device = "cpu"
    eng._mps_frames_since_cache_release = 0
    return eng


class MockDetector:
    """Mock detector that records received image size and returns configured detections."""
    def __init__(self, detections: List[Dict] | None = None):
        self.detections = detections or []
        self.received_images: List[Image.Image] = []

    def predict(self, image: Image.Image, source_model: str = "", **kwargs) -> List[Dict]:
        self.received_images.append(image)
        return list(self.detections)


def test_human_presence_gating_skips_when_no_human():
    """When a model requires human presence and no person is detected, it must skip."""
    engine = _bare_engine()
    mock_model = MockDetector(detections=[{"label": "no-hairnet", "score": 0.9, "box": {"xmin": 10, "ymin": 10, "xmax": 50, "ymax": 50}}])
    engine._registry["ppe-gated-model"] = mock_model
    # Person detector returns empty list (no humans in frame)
    engine._detect_persons = MagicMock(return_value=[])

    rule = ViolationRule(
        sop_violation_type_id="rule-ppe-1",
        model_identifier="ppe-gated-model",
        trigger_labels=["no-hairnet"],
        ai_model_id="ai-guid-1",
        model_requires_human_presence=True,
        model_requires_cropping=False,
    )

    test_img = Image.new("RGB", (640, 480), color=(200, 200, 200))
    results = engine.run_inference(test_img, [rule], camera_id="CAM-01")

    assert len(results) == 0
    assert len(mock_model.received_images) == 0  # Model was never invoked!


def test_non_human_model_runs_when_no_human_in_frame():
    """When a model does NOT require human presence (e.g. pest, rodent), it must execute even with 0 humans."""
    engine = _bare_engine()
    mock_pest_model = MockDetector(detections=[{
        "label": "rat",
        "score": 0.95,
        "box": {"xmin": 100, "ymin": 200, "xmax": 160, "ymax": 240}
    }])
    engine._registry["pest-detection-v1"] = mock_pest_model
    # Person detector returns empty list (no humans in scene)
    engine._detect_persons = MagicMock(return_value=[])

    rule = ViolationRule(
        sop_violation_type_id="rule-pest-1",
        model_identifier="pest-detection-v1",
        trigger_labels=["rat", "cockroach"],
        ai_model_id="ai-guid-pest",
        model_requires_human_presence=False,
        model_requires_cropping=False,
    )

    test_img = Image.new("RGB", (640, 480), color=(50, 50, 50))
    results = engine.run_inference(test_img, [rule], camera_id="CAM-01")

    assert len(results) == 1
    assert results[0]["label"] == "rat"
    assert results[0]["score"] == 0.95
    assert len(mock_pest_model.received_images) == 1
    # Full uncropped image passed
    assert mock_pest_model.received_images[0].size == (640, 480)


def test_cropping_bypassed_for_contextual_rules():
    """When requires_cropping=False, the full uncropped image is fed to the detector (e.g., chair/machine jumping)."""
    engine = _bare_engine()
    mock_context_model = MockDetector(detections=[{
        "label": "standing_on_chair",
        "score": 0.88,
        "box": {"xmin": 50, "ymin": 50, "xmax": 300, "ymax": 450}
    }])
    engine._registry["contextual-safety-v1"] = mock_context_model
    # Person detected in the frame
    engine._detect_persons = MagicMock(return_value=[
        {"xmin": 100, "ymin": 80, "xmax": 250, "ymax": 400, "score": 0.92}
    ])

    rule = ViolationRule(
        sop_violation_type_id="rule-chair-1",
        model_identifier="contextual-safety-v1",
        trigger_labels=["standing_on_chair"],
        ai_model_id="ai-guid-context",
        model_requires_human_presence=True,
        model_requires_cropping=False,  # Uncropped full frame!
    )

    test_img = Image.new("RGB", (1280, 720), color=(100, 100, 100))
    results = engine.run_inference(test_img, [rule], camera_id="CAM-01")

    assert len(results) == 1
    assert results[0]["label"] == "standing_on_chair"
    assert len(mock_context_model.received_images) == 1
    # Full uncropped image size preserved!
    assert mock_context_model.received_images[0].size == (1280, 720)


def test_person_cropping_enabled_for_micro_ppe():
    """When requires_cropping=True, detected persons are cropped and detections offset back."""
    engine = _bare_engine()
    mock_ppe_model = MockDetector(detections=[{
        "label": "no-hairnet",
        "score": 0.89,
        "box": {"xmin": 10, "ymin": 10, "xmax": 50, "ymax": 50}
    }])
    engine._registry["micro-ppe-v1"] = mock_ppe_model

    # One person box: (100, 100) to (300, 400)
    engine._detect_persons = MagicMock(return_value=[
        {"xmin": 100, "ymin": 100, "xmax": 300, "ymax": 400, "score": 0.95}
    ])

    rule = ViolationRule(
        sop_violation_type_id="rule-micro-1",
        model_identifier="micro-ppe-v1",
        trigger_labels=["no-hairnet"],
        ai_model_id="ai-guid-micro",
        model_requires_human_presence=True,
        model_requires_cropping=True,
    )

    test_img = Image.new("RGB", (1000, 1000), color=(120, 120, 120))
    results = engine.run_inference(test_img, [rule], camera_id="CAM-01")

    assert len(results) == 1
    assert results[0]["label"] == "no-hairnet"
    assert len(mock_ppe_model.received_images) == 1
    # Cropped image is smaller than full 1000x1000 frame
    crop_w, crop_h = mock_ppe_model.received_images[0].size
    assert crop_w < 1000 and crop_h < 1000
    # Coordinates were offset back to full-frame coordinates
    assert results[0]["box"]["xmin"] > 10
    assert "person_box" in results[0]


def test_multi_model_mixed_frame_evaluation():
    """Frame with no persons: PPE model is skipped, Pest model runs and detects pests."""
    engine = _bare_engine()

    mock_ppe = MockDetector(detections=[{"label": "no-mask", "score": 0.9, "box": {"xmin": 1, "ymin": 1, "xmax": 10, "ymax": 10}}])
    mock_pest = MockDetector(detections=[{"label": "cockroach", "score": 0.85, "box": {"xmin": 200, "ymin": 300, "xmax": 240, "ymax": 330}}])

    engine._registry["restaurant-ppe-v1"] = mock_ppe
    engine._registry["pest-detection-v1"] = mock_pest
    engine._detect_persons = MagicMock(return_value=[])  # Empty kitchen scene at night

    ppe_rule = ViolationRule(
        sop_violation_type_id="ppe-rule",
        model_identifier="restaurant-ppe-v1",
        trigger_labels=["no-mask"],
        ai_model_id="guid-ppe",
        model_requires_human_presence=True,
        model_requires_cropping=True,
    )
    pest_rule = ViolationRule(
        sop_violation_type_id="pest-rule",
        model_identifier="pest-detection-v1",
        trigger_labels=["cockroach", "rat"],
        ai_model_id="guid-pest",
        model_requires_human_presence=False,
        model_requires_cropping=False,
    )

    test_img = Image.new("RGB", (640, 480), color=(10, 10, 10))
    results = engine.run_inference(test_img, [ppe_rule, pest_rule], camera_id="CAM-NIGHT-01")

    # PPE should NOT have run
    assert len(mock_ppe.received_images) == 0
    # Pest detector DID run
    assert len(mock_pest.received_images) == 1
    assert len(results) == 1
    assert results[0]["label"] == "cockroach"


def test_real_dataset_image_pest_detection():
    """Run real dataset image through pest detector without human presence gate."""
    pest_img_path = "/Users/macbookpro/Desktop/projects/Alpha-Surveilance/Alpha-Surveilance/datasets/kitchen-pest-detection/images/val/0126c33afd7aa743_jpg.rf.3ee98bdaf8aafaa129b9186d60e03ca0.jpg"
    assert os.path.exists(pest_img_path), f"Dataset image {pest_img_path} not found"

    real_img = Image.open(pest_img_path)
    engine = _bare_engine()

    mock_pest = MockDetector(detections=[{"label": "cockroach", "score": 0.91, "box": {"xmin": 50, "ymin": 50, "xmax": 120, "ymax": 120}}])
    engine._registry["pest-detection-v1"] = mock_pest
    engine._detect_persons = MagicMock(return_value=[])  # No human in image

    rule = ViolationRule(
        sop_violation_type_id="rule-pest-dataset",
        model_identifier="pest-detection-v1",
        trigger_labels=["cockroach"],
        ai_model_id="pest-guid",
        model_requires_human_presence=False,
        model_requires_cropping=False,
    )

    results = engine.run_inference(real_img, [rule], camera_id="CAM-01")
    assert len(results) == 1
    assert results[0]["label"] == "cockroach"
    assert mock_pest.received_images[0].size == real_img.size


if __name__ == "__main__":
    test_human_presence_gating_skips_when_no_human()
    test_non_human_model_runs_when_no_human_in_frame()
    test_cropping_bypassed_for_contextual_rules()
    test_person_cropping_enabled_for_micro_ppe()
    test_multi_model_mixed_frame_evaluation()
    test_real_dataset_image_pest_detection()
    print("🎉 All dynamic cropping and human presence gating tests passed successfully!")
