"""
Restaurant PPE detector adapter.

This module is intentionally narrow: restaurant hairnet/mask decisions must come
from a trained YOLO model, not from person-box spatial heuristics. The model
should emit violation classes directly.
"""
import logging
import os
import threading
from typing import Dict, List, Optional

from PIL import Image

logger = logging.getLogger("vision-service.inference.restaurant-ppe")


MODEL_IDS = {"restaurant-ppe-v1", "restaurant-hygiene-v1"}

LABEL_ALIASES = {
    "no-hairnet": "no-hairnet",
    "no_hairnet": "no-hairnet",
    "missing-hairnet": "no-hairnet",
    "missing_hairnet": "no-hairnet",
    "hairnet-missing": "no-hairnet",
    "hairnet_missing": "no-hairnet",
    "person-without-hairnet": "no-hairnet",
    "person_without_hairnet": "no-hairnet",
    "without-hairnet": "no-hairnet",
    "no-mask": "no-mask",
    "no_mask": "no-mask",
    "missing-mask": "no-mask",
    "missing_mask": "no-mask",
    "mask-missing": "no-mask",
    "mask_missing": "no-mask",
    "person-without-mask": "no-mask",
    "person_without_mask": "no-mask",
    "without-mask": "no-mask",
    "face-mask-missing": "no-mask",
    "face_mask_missing": "no-mask",
    "visible-face-no-mask": "no-mask",
    "visible_face_no_mask": "no-mask",
}


def normalize_violation_label(raw_label: str) -> Optional[str]:
    """
    Normalize trained model class names to the canonical violation vocabulary.

    Positive classes such as "mask", "hairnet", "back-of-head", or "person" are
    intentionally not mapped. They are useful during model training/validation,
    but should not create violation events.
    """
    cleaned = str(raw_label or "").strip().lower().replace(" ", "-")
    return LABEL_ALIASES.get(cleaned)


class RestaurantPpeDetector:
    """
    Thin, thread-safe wrapper around a fine-tuned YOLOv11 restaurant PPE model.

    Expected production classes:
      - no-hairnet
      - no-mask

    If the training project uses richer class names, keep them violation-only
    and add aliases above. Do not map back-of-head or compliant PPE classes to a
    violation; that would reintroduce rule-based assumptions.
    """

    def __init__(
        self,
        *,
        model_id: str,
        weights_path: str,
        yolo_cls,
        device: str,
        confidence: float,
        image_size: int,
    ):
        self.model_id = model_id
        self.weights_path = weights_path
        self.device = device
        self.confidence = confidence
        self.image_size = image_size
        self._lock = threading.Lock()
        self._model = None

        if not weights_path:
            logger.error("Restaurant PPE model path is empty. Set RESTAURANT_PPE_MODEL_PATH.")
            return

        if not os.path.exists(weights_path):
            logger.error(
                "Restaurant PPE model weights not found at %s. "
                "Export the Roboflow-trained YOLOv11 weights and mount them there.",
                weights_path,
            )
            return

        self._model = yolo_cls(weights_path)
        logger.info("Loaded restaurant PPE model '%s' from %s", model_id, weights_path)

    @property
    def available(self) -> bool:
        return self._model is not None

    def predict(self, pil_image: Image.Image, source_model: str) -> List[Dict]:
        if not self._model:
            return []

        with self._lock:
            det_results = self._model.predict(
                pil_image,
                conf=self.confidence,
                imgsz=self.image_size,
                device=self.device,
                verbose=False,
            )

        detections: List[Dict] = []
        for result in det_results:
            boxes = result.boxes
            for idx in range(len(boxes)):
                raw_box = boxes[idx].xyxy[0].cpu().numpy()
                label_idx = int(boxes[idx].cls[0])
                raw_label = str(result.names[label_idx]).lower()
                label = normalize_violation_label(raw_label)

                if not label:
                    continue

                score = float(boxes[idx].conf[0])
                detections.append(
                    {
                        "label": label,
                        "raw_label": raw_label,
                        "score": score,
                        "box": {
                            "xmin": int(raw_box[0]),
                            "ymin": int(raw_box[1]),
                            "xmax": int(raw_box[2]),
                            "ymax": int(raw_box[3]),
                        },
                        "source_model": source_model,
                        "model_family": "restaurant-ppe",
                    }
                )

        return detections
