"""
inference/inference_engine.py
Modularized inference execution engine.

Restaurant PPE compliance uses a fine-tuned YOLO model only. The previous
zero-shot hygiene fallback was intentionally removed because hairnet and mask
violations must come from trained model classes, not prompt-based guesses.
"""
import logging
import threading
from typing import Dict, List

import numpy as np
import torch
from PIL import Image

import config
from inference.restaurant_ppe import MODEL_IDS as RESTAURANT_PPE_MODEL_IDS
from inference.restaurant_ppe import RestaurantPpeDetector
from inference.model_loader import ensure_model_local

try:
    from ultralytics import YOLO, YOLOWorld

    HAS_ULTRALYTICS = True
except ImportError:
    YOLO = None
    YOLOWorld = None
    HAS_ULTRALYTICS = False

from inference_sdk import InferenceHTTPClient
from transformers import pipeline

logger = logging.getLogger("vision-service.inference")


class InferenceEngine:
    def __init__(self):
        self._legacy_lock = threading.Lock()
        self._registry: Dict[str, object] = {}

        if torch.backends.mps.is_available():
            self.device = "mps"
            logger.info("Apple Silicon MPS acceleration available.")
        elif torch.cuda.is_available():
            self.device = "cuda"
            logger.info("NVIDIA CUDA acceleration available.")
        else:
            self.device = "cpu"
            logger.info("Using CPU for inference.")

        self._load_models()

    def _load_models(self):
        logger.info("Loading object detection models into registry...")

        if HAS_ULTRALYTICS:
            try:
                import os

                os.makedirs("/tmp/models", exist_ok=True)

                logger.info("Loading YOLOv11n person detector...")
                self._registry["human-detection-v1"] = YOLO("/tmp/models/yolo11n.pt")

                # Download restaurant PPE weights from S3 if not already cached locally
                ppe_weights_path = ensure_model_local()

                restaurant_detector = RestaurantPpeDetector(
                    model_id=config.RESTAURANT_PPE_MODEL_IDENTIFIER,
                    weights_path=ppe_weights_path,
                    yolo_cls=YOLO,
                    device=self.device,
                    confidence=config.MIN_CONFIDENCE_RESTAURANT_PPE,
                    image_size=config.RESTAURANT_PPE_IMAGE_SIZE,
                )
                if restaurant_detector.available:
                    for model_id in RESTAURANT_PPE_MODEL_IDS:
                        self._registry[model_id] = restaurant_detector
                else:
                    logger.error(
                        "Restaurant PPE detector is unavailable. "
                        "Hairnet/mask rules will not emit violations until weights are mounted."
                    )

            except Exception as e:
                logger.error("Failed to load Ultralytics models: %s", e)

        try:
            if "human-detection-v1" not in self._registry:
                logger.warning("Using legacy YOLOS-tiny person detector.")
                self._registry["human-detection-v1-legacy"] = pipeline(
                    "object-detection",
                    model="hustvl/yolos-tiny",
                    device=self.device if self.device != "mps" else -1,
                )
        except Exception as e:
            logger.error("Critical failure loading legacy person detector: %s", e)

        logger.info("Initializing Roboflow inference client...")
        try:
            self._roboflow_client = InferenceHTTPClient(
                api_url="https://detect.roboflow.com",
                api_key=config.ROBOFLOW_API_KEY,
            )
        except Exception as e:
            logger.error("Failed to initialize Roboflow client. Check ROBOFLOW_API_KEY. %s", e)
            self._roboflow_client = None

        self._roboflow_map = {
            "construction-site-safety-v1": "construction-site-safety/1",
        }

        logger.info("Models loaded and ready on %s", self.device)

    def run_inference(self, pil_image: Image.Image, active_rules: List) -> List[Dict]:
        """
        Runs inference on the provided image based on active camera rules.
        """
        results: List[Dict] = []
        unique_model_ids = {rule.model_identifier for rule in active_rules}

        for model_id in unique_model_ids:
            model = self._registry.get(model_id)

            if isinstance(model, RestaurantPpeDetector):
                results.extend(model.predict(pil_image, source_model=model_id))
                continue

            was_roboflow = False
            if model_id in self._roboflow_map:
                results.extend(self._run_roboflow_inference(pil_image, self._roboflow_map[model_id]))
                was_roboflow = True
            elif model_id in ("construction-site-safety/1",):
                results.extend(self._run_roboflow_inference(pil_image, model_id))
                was_roboflow = True

            if was_roboflow:
                continue

            if not model:
                model = self._registry.get(f"{model_id}-legacy")
                if not model:
                    logger.error("Model '%s' not found in registry.", model_id)
                    continue

            try:
                if HAS_ULTRALYTICS and isinstance(model, (YOLO, YOLOWorld)):
                    det_results = model.predict(pil_image, conf=0.25, device=self.device, verbose=False)

                    for result in det_results:
                        boxes = result.boxes
                        for idx in range(len(boxes)):
                            raw_box = boxes[idx].xyxy[0].cpu().numpy()
                            label_idx = int(boxes[idx].cls[0])
                            label = result.names[label_idx]
                            score = float(boxes[idx].conf[0])

                            results.append(
                                {
                                    "label": label.lower(),
                                    "score": score,
                                    "box": {
                                        "xmin": int(raw_box[0]),
                                        "ymin": int(raw_box[1]),
                                        "xmax": int(raw_box[2]),
                                        "ymax": int(raw_box[3]),
                                    },
                                    "source_model": model_id,
                                }
                            )
                else:
                    with self._legacy_lock:
                        detections = model(pil_image, threshold=0.25)
                        for detection in detections:
                            detection["source_model"] = model_id
                            results.append(detection)

            except Exception as e:
                logger.error("Inference failed for model %s: %s", model_id, e)

        return results

    def _run_roboflow_inference(self, pil_image: Image.Image, model_id: str) -> List[Dict]:
        if not self._roboflow_client:
            return []

        try:
            np_img = np.array(pil_image)
            result = self._roboflow_client.infer(np_img, model_id=model_id)
            predictions = result.get("predictions", [])

            std_results = []
            for prediction in predictions:
                cx, cy = prediction["x"], prediction["y"]
                w, h = prediction["width"], prediction["height"]

                std_results.append(
                    {
                        "label": prediction.get("class", "unknown").lower(),
                        "score": prediction.get("confidence", 0.0),
                        "box": {
                            "xmin": int(cx - (w / 2.0)),
                            "ymin": int(cy - (h / 2.0)),
                            "xmax": int(cx + (w / 2.0)),
                            "ymax": int(cy + (h / 2.0)),
                        },
                        "source_model": model_id,
                    }
                )
            return std_results
        except Exception as e:
            logger.error("Roboflow API failed: %s", e)
            return []


if __name__ == "__main__":
    pass
