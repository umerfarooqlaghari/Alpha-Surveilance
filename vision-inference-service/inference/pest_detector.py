"""
inference/pest_detector.py
==========================
YOLO-based pest detector for cockroach, lizard, and rat detection.

Unlike the PPE detector this runs on the FULL FRAME — no person-crop gate.
Pests appear in the environment (floor, walls, surfaces) not on people.

Model classes (matches kitchen-pest-detection dataset):
  0: cockroach
  1: lizard
  2: rat

Model identifier registered in the inference engine: pest-detection-v1
S3 key: models/kitchen-pest-yolo11m.pt
"""
import logging
import threading
from typing import Dict, List, Optional

from PIL import Image

import config

logger = logging.getLogger("vision-service.inference.pest")

MODEL_IDS = {"pest-detection-v1"}

# Normalise any label variants the model might emit
LABEL_ALIASES: Dict[str, str] = {
    "cockroach":  "cockroach",
    "cock_roach": "cockroach",
    "cock-roach": "cockroach",
    "lizard":     "lizard",
    "gecko":      "lizard",
    "rat":        "rat",
    "mouse":      "rat",
    "rodent":     "rat",
}


def normalize_pest_label(raw: str) -> Optional[str]:
    return LABEL_ALIASES.get(str(raw).lower().strip().replace(" ", "_"))


class PestDetector:
    """
    Thin wrapper around a YOLO model for pest detection.
    Loaded once at startup; thread-safe for concurrent frame calls.
    """

    def __init__(self, weights_path: str, yolo_cls, device: str = "cpu",
                 confidence: float = 0.50, image_size: int = 640):
        self._confidence  = confidence
        self._image_size  = image_size
        self._device      = device
        self._model       = None
        self.available    = False
        self._predict_mutex = threading.Lock()
        self._batcher = None

        if not weights_path:
            logger.warning("PestDetector: no weights path provided — detector disabled.")
            return

        import os
        if not os.path.exists(weights_path):
            logger.warning(
                "PestDetector: weights file not found at %s — "
                "train the pest model on Colab and upload to S3 first.",
                weights_path,
            )
            return

        try:
            self._model   = yolo_cls(weights_path)
            self.available = True
            logger.info("✅ PestDetector loaded: %s (device=%s)", weights_path, device)
            if getattr(config, "DYNAMIC_BATCHING_ENABLED", True):
                self._init_batcher()
        except Exception as e:
            logger.error("PestDetector failed to load %s: %s", weights_path, e)

    def _init_batcher(self):
        try:
            from inference.dynamic_batcher import DynamicBatchScheduler

            self._batcher = DynamicBatchScheduler(
                self._raw_batch_predict,
                name="pest-detector",
                max_batch_size=getattr(config, "DYNAMIC_BATCH_MAX_SIZE", 8),
                max_wait_ms=getattr(config, "DYNAMIC_BATCH_WAIT_MS", 5.0),
            )
        except Exception as e:
            logger.warning("Could not initialize DynamicBatchScheduler for PestDetector: %s", e)
            self._batcher = None

    def _raw_batch_predict(self, batch_images: List[Image.Image]) -> List[List[Dict]]:
        if not self._model or not batch_images:
            return [[] for _ in batch_images]

        try:
            with self._predict_mutex:
                results = self._model(
                    batch_images,
                    conf=self._confidence,
                    imgsz=self._image_size,
                    device=self._device,
                    verbose=False,
                )
        except Exception as e:
            logger.error("PestDetector._raw_batch_predict failed: %s", e)
            return [[] for _ in batch_images]

        batch_dets: List[List[Dict]] = []
        for r in results:
            dets = []
            if r.boxes is not None:
                for i in range(len(r.boxes)):
                    raw_box   = r.boxes[i].xyxy[0].cpu().numpy()
                    label_idx = int(r.boxes[i].cls[0])
                    raw_label = r.names[label_idx]
                    score     = float(r.boxes[i].conf[0])

                    canonical = normalize_pest_label(raw_label)
                    if canonical is None:
                        continue

                    dets.append({
                        "label":        canonical,
                        "raw_label":    raw_label,
                        "score":        score,
                        "box": {
                            "xmin": int(raw_box[0]),
                            "ymin": int(raw_box[1]),
                            "xmax": int(raw_box[2]),
                            "ymax": int(raw_box[3]),
                        },
                        "source_model": "pest-detection-v1",
                        "model_family": "pest-detection",
                    })
            batch_dets.append(dets)
        return batch_dets

    def predict(self, pil_image: Image.Image, source_model: str = "pest-detection-v1") -> List[Dict]:
        if not self.available or self._model is None:
            return []

        if self._batcher is not None and getattr(config, "DYNAMIC_BATCHING_ENABLED", True):
            try:
                dets = self._batcher.submit(pil_image)
                for d in dets:
                    d["source_model"] = source_model
                return dets
            except Exception as e:
                logger.error("PestDetector dynamic batch failed; falling back to direct lock: %s", e)

        raw_res = self._raw_batch_predict([pil_image])
        dets = raw_res[0] if raw_res else []
        for d in dets:
            d["source_model"] = source_model
        return dets

    def close(self):
        """Cleanly terminate background scheduler thread."""
        if self._batcher is not None:
            self._batcher.stop()
            self._batcher = None

    def __del__(self):
        try:
            self.close()
        except Exception:
            pass
