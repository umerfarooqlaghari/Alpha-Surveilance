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

import config
from inference.preprocess import enhance_low_light

logger = logging.getLogger("vision-service.inference.restaurant-ppe")


MODEL_IDS = {
    "restaurant-ppe-v1",
    "restaurant-hygiene-v1",
    "kitchen-hygiene-yolo11n-v1",
    "kitchen-hygiene-yolo11m-v1",
    "kitchen-hygiene-yolo11m-v2",
}

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
    "no-glove": "no-glove",
    "no-gloves": "no-glove",
    "no_glove": "no-glove",
    "no_gloves": "no-glove",
    "missing-glove": "no-glove",
    "missing-gloves": "no-glove",
    "missing_glove": "no-glove",
    "missing_gloves": "no-glove",
    "glove-missing": "no-glove",
    "gloves-missing": "no-glove",
    "glove_missing": "no-glove",
    "gloves_missing": "no-glove",
    "person-without-glove": "no-glove",
    "person-without-gloves": "no-glove",
    "person_without_glove": "no-glove",
    "person_without_gloves": "no-glove",
    "without-glove": "no-glove",
    "without-gloves": "no-glove",
    "incorrect-mask": "incorrect-mask",
    "incorrect_mask": "incorrect-mask",
    "improper-mask": "incorrect-mask",
    "improper_mask": "incorrect-mask",
    "mask-worn-incorrectly": "incorrect-mask",
    "mask_worn_incorrectly": "incorrect-mask",
    "mask-below-nose": "incorrect-mask",
    "mask_below_nose": "incorrect-mask",
}

# ── Spatial AND-gate helpers ──────────────────────────────────────────────────

def _iou(box_a: tuple, box_b: tuple) -> float:
    """Compute IoU between two (xmin, ymin, xmax, ymax) boxes."""
    ix1 = max(box_a[0], box_b[0])
    iy1 = max(box_a[1], box_b[1])
    ix2 = min(box_a[2], box_b[2])
    iy2 = min(box_a[3], box_b[3])
    inter = max(0.0, ix2 - ix1) * max(0.0, iy2 - iy1)
    if inter == 0.0:
        return 0.0
    area_a = (box_a[2] - box_a[0]) * (box_a[3] - box_a[1])
    area_b = (box_b[2] - box_b[0]) * (box_b[3] - box_b[1])
    union = area_a + area_b - inter
    return inter / union if union > 0 else 0.0


# Minimum IoU between a violation box and a compliance box to consider them
# belonging to the same spatial region (same person / same PPE site).
# A low threshold is intentional — violation and compliance boxes won't be
# pixel-perfect matches, but they must overlap meaningfully.
PPE_GATE_SPATIAL_IOU: float = 0.10
# Run the model at this lower conf to capture positive compliance classes
# (mask / glove / hairnet present) even when they score below the violation
# threshold.  Their scores are used exclusively to suppress contradicting
# violation detections — they are never emitted as violations themselves.
PPE_GATE_SCAN_CONF: float = 0.15
# A positive class scoring at or above this value blocks the paired violation.
PPE_GATE_COMPLIANCE_CONF: float = 0.25

# Maps raw YOLO class names → PPE category (used to populate compliance_scores).
COMPLIANCE_LABEL_MAP: dict = {
    "mask": "mask",
    "glove": "glove",
    "gloves": "glove",
    "hairnet": "hairnet",
    "hair-cover": "hairnet",
}

# Maps canonical violation label → the compliance category that blocks it.
PPE_GATE: dict = {
    "no-glove": "glove",
    "no-hairnet": "hairnet",
    "no-mask": "mask",
    "incorrect-mask": "mask",
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
            - no-glove
            - incorrect-mask

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

        # Low-light enhancement (CLAHE + conditional gamma). Cheap (~5ms)
        # and helps dim CCTV scenes. Gated via RESTAURANT_PPE_ENHANCE_LOWLIGHT.
        if config.RESTAURANT_PPE_ENHANCE_LOWLIGHT:
            pil_image = enhance_low_light(pil_image)

        # AND-gate: scan at a lower confidence to capture positive compliance
        # classes (mask/glove/hairnet) that may score below the violation
        # threshold but still indicate the PPE is present.
        scan_conf = min(self.confidence, PPE_GATE_SCAN_CONF)
        with self._lock:
            det_results = self._model.predict(
                pil_image,
                conf=scan_conf,
                imgsz=self.image_size,
                device=self.device,
                verbose=False,
            )

        # First pass: collect compliance detections (box + score) per category.
        # Keyed by category → list of (xmin, ymin, xmax, ymax, score).
        compliance_detections: Dict[str, List[tuple]] = {}
        for result in det_results:
            for idx in range(len(result.boxes)):
                label_idx = int(result.boxes[idx].cls[0])
                raw_label = str(result.names[label_idx]).lower().replace("_", "-").replace(" ", "-")
                score = float(result.boxes[idx].conf[0])
                cat = COMPLIANCE_LABEL_MAP.get(raw_label)
                if cat is not None:
                    box = result.boxes[idx].xyxy[0].cpu().numpy()
                    entry = (int(box[0]), int(box[1]), int(box[2]), int(box[3]), score)
                    compliance_detections.setdefault(cat, []).append(entry)

        if compliance_detections:
            logger.debug("AND-gate compliance detections: %s",
                         {k: len(v) for k, v in compliance_detections.items()})

        # Second pass: emit violations, applying the AND-gate.
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
                if score < self.confidence:
                    continue  # below configured violation threshold

                # AND-gate: suppress violation only if a spatially overlapping
                # compliance detection exists for its paired PPE category.
                # Frame-global suppression is intentionally avoided — one
                # compliant person must not silence violations on another.
                gating_cat = PPE_GATE.get(label)
                if gating_cat and gating_cat in compliance_detections:
                    vbox = (int(raw_box[0]), int(raw_box[1]), int(raw_box[2]), int(raw_box[3]))
                    best_iou = max(
                        _iou(vbox, (c[0], c[1], c[2], c[3]))
                        for c in compliance_detections[gating_cat]
                    )
                    best_score = max(c[4] for c in compliance_detections[gating_cat]
                                     if _iou(vbox, (c[0], c[1], c[2], c[3])) >= PPE_GATE_SPATIAL_IOU) \
                        if best_iou >= PPE_GATE_SPATIAL_IOU else 0.0
                    if best_iou >= PPE_GATE_SPATIAL_IOU and best_score >= PPE_GATE_COMPLIANCE_CONF:
                        logger.info(
                            "AND-gate suppressed '%s' (score=%.2f): '%s' detected at %.2f (IoU=%.2f)",
                            label, score, gating_cat, best_score, best_iou,
                        )
                        continue

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
