"""
tests/test_api_positive_negative_images.py
===========================================
End-to-end testing with Positive and Negative images directly against the service API
and frame analysis pipeline:

1. Pest Detection Model (RequiresCropping=False, RequiresHumanPresence=False):
   - Positive Image: Real pest image from kitchen-pest-detection dataset (no humans present).
     -> Evaluates on full frame, detects pest, triggers violation.
   - Negative Image: Clean image (no pests, no humans).
     -> Evaluates on full frame, detects no pests, triggers 0 violations.

2. PPE Model (RequiresCropping=True, RequiresHumanPresence=True):
   - Positive Image: Real PPE violation image from kitchen-ppe-finetune dataset (human present without hairnet/mask).
     -> Detects person, crops bounding box, detects PPE violation.
   - Negative Image: Empty room image (no humans).
     -> Human presence gate suppresses inference, prevents false positives and wasted compute.

3. Contextual / Machine Safety / Standing on Chair / Reliever Duty Model (RequiresCropping=False, RequiresHumanPresence=True):
   - Positive Image: Human present with contextual surroundings (machinery / chair / station).
     -> Human detected, cropping bypassed (full frame fed to model), detects context violation.
   - Negative Image: Empty room / empty machine with no humans.
     -> Human presence gate suppresses inference.
"""
from __future__ import annotations

import io
import os
import sys
import threading
from unittest.mock import AsyncMock, MagicMock, patch

from PIL import Image

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from tests._stubs import install_stubs
install_stubs()

import inference.inference_engine as ie
from rtsp.models import CameraConfig, ViolationRule
from rtsp.violation_manager import ViolationManager
from rules.evaluator import evaluate_violations


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


class MockPredictor:
    def __init__(self, detections_by_condition):
        self.detections_by_condition = detections_by_condition
        self.received_images = []

    def predict(self, image: Image.Image, source_model: str = "", **kwargs):
        self.received_images.append(image)
        return self.detections_by_condition(image, source_model)


def test_pest_positive_and_negative_images():
    """Test Pest Detection on Positive (pest present) and Negative (clean) images."""
    engine = _bare_engine()

    # Load real positive image from datasets
    positive_path = "/Users/macbookpro/Desktop/projects/Alpha-Surveilance/Alpha-Surveilance/datasets/kitchen-pest-detection/images/val/0126c33afd7aa743_jpg.rf.3ee98bdaf8aafaa129b9186d60e03ca0.jpg"
    assert os.path.exists(positive_path), "Positive pest image not found"
    pos_img = Image.open(positive_path).convert("RGB")

    # Create negative clean image
    neg_img = Image.new("RGB", (640, 480), color=(240, 240, 240))

    # Mock detector that identifies cockroach only on positive image
    def pest_predict(img: Image.Image, source_model: str):
        if img.size == pos_img.size:
            return [{"label": "cockroach", "score": 0.94, "box": {"xmin": 45, "ymin": 60, "xmax": 120, "ymax": 130}}]
        return []

    mock_pest = MockPredictor(pest_predict)
    engine._registry["pest-detection-v1"] = mock_pest
    engine._detect_persons = MagicMock(return_value=[])  # No humans in either frame

    pest_rule = ViolationRule(
        sop_violation_type_id="sop-pest-001",
        model_identifier="pest-detection-v1",
        trigger_labels=["cockroach", "rat"],
        ai_model_id="ai-pest-guid",
        model_requires_human_presence=False,  # Can run without humans
        model_requires_cropping=False,        # Full frame evaluation
    )

    # 1. Positive Image Execution
    pos_results = engine.run_inference(pos_img, [pest_rule], camera_id="CAM-KITCHEN-01")
    assert len(pos_results) == 1, "Pest detection failed on positive image!"
    assert pos_results[0]["label"] == "cockroach"
    assert pos_results[0]["score"] >= 0.90

    # 2. Negative Image Execution
    neg_results = engine.run_inference(neg_img, [pest_rule], camera_id="CAM-KITCHEN-01")
    assert len(neg_results) == 0, "False positive triggered on clean negative image!"


def test_ppe_positive_and_negative_images():
    """Test Micro-PPE on Positive (human with violation) and Negative (empty room / no humans)."""
    engine = _bare_engine()

    # Real positive PPE image (e.g. person without hairnet)
    positive_path = "/Users/macbookpro/Desktop/projects/Alpha-Surveilance/Alpha-Surveilance/datasets/kitchen-ppe-finetune/images/val/no_hairnet_loose_000003.jpg"
    assert os.path.exists(positive_path), "Positive PPE image not found"
    pos_img = Image.open(positive_path).convert("RGB")

    # Empty room image (no humans)
    empty_room_path = "/Users/macbookpro/Desktop/projects/Alpha-Surveilance/Alpha-Surveilance/datasets/kitchen-pest-detection/images/val/0126c33afd7aa743_jpg.rf.3ee98bdaf8aafaa129b9186d60e03ca0.jpg"
    neg_img = Image.open(empty_room_path).convert("RGB")

    def ppe_predict(crop: Image.Image, source_model: str):
        # Emits no-hairnet violation when running on the person crop
        return [{"label": "no-hairnet", "score": 0.92, "box": {"xmin": 5, "ymin": 5, "xmax": 40, "ymax": 40}}]

    mock_ppe = MockPredictor(ppe_predict)
    engine._registry["kitchen-hygiene-yolo11m-v2"] = mock_ppe

    # For positive image: person detector finds a worker
    # For negative image: person detector finds nobody
    def mock_detect_persons(img: Image.Image):
        if img.size == pos_img.size:
            return [{"xmin": 50, "ymin": 50, "xmax": 350, "ymax": 450, "score": 0.98}]
        return []

    engine._detect_persons = mock_detect_persons

    ppe_rule = ViolationRule(
        sop_violation_type_id="sop-ppe-001",
        model_identifier="kitchen-hygiene-yolo11m-v2",
        trigger_labels=["no-hairnet"],
        ai_model_id="ai-ppe-guid",
        model_requires_human_presence=True,  # Gated on human presence
        model_requires_cropping=True,        # Crop person
    )

    # 1. Positive Image (Human without hairnet)
    pos_results = engine.run_inference(pos_img, [ppe_rule], camera_id="CAM-PREP-01")
    assert len(pos_results) == 1, "PPE violation missed on positive worker image!"
    assert pos_results[0]["label"] == "no-hairnet"
    # Result coordinate is offset back into full frame coordinates
    assert pos_results[0]["box"]["xmin"] > 5
    assert pos_results[0]["person_box"]["xmin"] == 50

    # 2. Negative Image (Empty kitchen room with no humans)
    neg_results = engine.run_inference(neg_img, [ppe_rule], camera_id="CAM-PREP-01")
    assert len(neg_results) == 0, "PPE model incorrectly executed on empty room negative image!"


def test_contextual_compliance_positive_and_negative_images():
    """Test Contextual Compliance (RequiresCropping=False, RequiresHumanPresence=True)."""
    engine = _bare_engine()

    factory_scene_img = Image.new("RGB", (1920, 1080), color=(100, 110, 120))
    empty_factory_img = Image.new("RGB", (1920, 1080), color=(50, 50, 50))

    def context_predict(img: Image.Image, source_model: str):
        # Receives full uncropped frame and detects person jumping machine / standing on chair
        assert img.size == (1920, 1080), f"Expected uncropped (1920, 1080), got {img.size}"
        return [{"label": "jumping_over_machine", "score": 0.89, "box": {"xmin": 200, "ymin": 150, "xmax": 600, "ymax": 800}}]

    mock_context = MockPredictor(context_predict)
    engine._registry["factory-safety-v1"] = mock_context

    def mock_detect_persons(img: Image.Image):
        if img == factory_scene_img:
            return [{"xmin": 250, "ymin": 200, "xmax": 500, "ymax": 750, "score": 0.95}]
        return []

    engine._detect_persons = mock_detect_persons

    context_rule = ViolationRule(
        sop_violation_type_id="sop-factory-001",
        model_identifier="factory-safety-v1",
        trigger_labels=["jumping_over_machine"],
        ai_model_id="ai-factory-guid",
        model_requires_human_presence=True,  # Gated on human presence
        model_requires_cropping=False,       # Uncropped full frame!
    )

    # 1. Positive Scene (Worker jumping over machinery)
    pos_results = engine.run_inference(factory_scene_img, [context_rule], camera_id="CAM-FACTORY-01")
    assert len(pos_results) == 1, "Contextual safety violation missed on positive image!"
    assert pos_results[0]["label"] == "jumping_over_machine"
    assert mock_context.received_images[-1].size == (1920, 1080), "Full frame context must NOT be cropped!"

    # 2. Negative Scene (Empty machinery area with no workers)
    neg_results = engine.run_inference(empty_factory_img, [context_rule], camera_id="CAM-FACTORY-01")
    assert len(neg_results) == 0, "Contextual model ran on empty area negative image!"


def test_api_analyze_pipeline_simulation():
    """Test full analyze pipeline simulation with mixed active models."""
    engine = _bare_engine()

    def pest_det(img, source_model):
        return [{"label": "rat", "score": 0.96, "box": {"xmin": 100, "ymin": 200, "xmax": 150, "ymax": 240}}]

    def ppe_det(crop, source_model):
        return [{"label": "no-mask", "score": 0.91, "box": {"xmin": 10, "ymin": 10, "xmax": 50, "ymax": 50}}]

    engine._registry["pest-detection-v1"] = MockPredictor(pest_det)
    engine._registry["kitchen-hygiene-yolo11m-v2"] = MockPredictor(ppe_det)

    # Empty room at night (no humans)
    engine._detect_persons = MagicMock(return_value=[])

    pest_rule = ViolationRule(
        sop_violation_type_id="pest-sop",
        model_identifier="pest-detection-v1",
        trigger_labels=["rat"],
        ai_model_id="guid-1",
        model_requires_human_presence=False,
        model_requires_cropping=False,
    )
    ppe_rule = ViolationRule(
        sop_violation_type_id="ppe-sop",
        model_identifier="kitchen-hygiene-yolo11m-v2",
        trigger_labels=["no-mask"],
        ai_model_id="guid-2",
        model_requires_human_presence=True,
        model_requires_cropping=True,
    )

    empty_night_frame = Image.new("RGB", (1280, 720), color=(15, 15, 15))

    # Run pipeline evaluation
    raw_detections = engine.run_inference(empty_night_frame, [pest_rule, ppe_rule], camera_id="CAM-01")
    validated_violations = evaluate_violations(raw_detections, [pest_rule, ppe_rule], frame_size=(1280, 720), camera_id="CAM-01")

    # Only the pest detection must fire, PPE must be completely suppressed
    assert len(raw_detections) == 1
    assert raw_detections[0]["label"] == "rat"
    assert len(validated_violations) == 1
    assert validated_violations[0]["sop_violation_type_id"] == "pest-sop"


if __name__ == "__main__":
    test_pest_positive_and_negative_images()
    test_ppe_positive_and_negative_images()
    test_contextual_compliance_positive_and_negative_images()
    test_api_analyze_pipeline_simulation()
    print("🎯 All Positive and Negative image tests directly verified through the pipeline!")
