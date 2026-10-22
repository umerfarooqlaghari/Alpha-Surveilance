"""
Restaurant PPE detector adapter.

This module is intentionally narrow: restaurant hairnet/mask decisions must come
from a trained YOLO model, not from person-box spatial heuristics. The model
should emit violation classes directly.
"""
import logging
import os
import threading
from typing import Dict, List, Optional, Tuple

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

# AND-gate suppression has been removed entirely - all detected violations are now emitted.

# M10 fix: operational toggles are read from config.py so they show up in
# log_config() and can be monkeypatched in tests. Defaults preserved.
import config as _vis_config
RESTAURANT_PPE_PREFER_NO_MASK_LABEL: bool = _vis_config.RESTAURANT_PPE_PREFER_NO_MASK_LABEL

# Oversized head-class boxes (covering most of a person crop) are typically
# poor localizations. Filter those to improve annotation quality.
RESTAURANT_PPE_ENABLE_OVERSIZE_FILTER: bool = _vis_config.RESTAURANT_PPE_ENABLE_OVERSIZE_FILTER
RESTAURANT_PPE_MAX_HEADBOX_AREA_RATIO: float = _vis_config.RESTAURANT_PPE_MAX_HEADBOX_AREA_RATIO

# Maps raw YOLO class names → PPE category (used to populate compliance_scores).
COMPLIANCE_LABEL_MAP: dict = {
    "mask": "mask",
    "glove": "glove",
    "gloves": "glove",
    "hairnet": "hairnet",
    "hair-cover": "hairnet",
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
        self._batcher = None

        if not weights_path:
            logger.error("Restaurant PPE model path is empty.")
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

        if self._model is not None and getattr(config, "DYNAMIC_BATCHING_ENABLED", True):
            self._init_batcher()

    def _init_batcher(self):
        try:
            from inference.dynamic_batcher import DynamicBatchScheduler

            self._batcher = DynamicBatchScheduler(
                self._raw_batch_predict,
                name=f"ppe-{self.model_id}",
                max_batch_size=getattr(config, "MAX_CROP_BATCH_SIZE", 8),
                max_wait_ms=getattr(config, "DYNAMIC_BATCH_WAIT_MS", 5.0),
            )
        except Exception as e:
            logger.warning(
                "Could not initialize DynamicBatchScheduler for PPE model '%s': %s",
                self.model_id,
                e,
            )
            self._batcher = None

    @property
    def available(self) -> bool:
        return self._model is not None

    def predict(self, pil_image: Image.Image, source_model: str) -> List[Dict]:
        """Single-crop prediction (calls predict_batch under the hood)."""
        res = self.predict_batch([pil_image], source_model=source_model)
        return res[0] if res else []

    def _raw_batch_predict(
        self, items: List[Tuple[Image.Image, str]]
    ) -> List[List[Dict]]:
        """Internal batched forward pass and violation parsing."""
        if not self._model or not items:
            return [[] for _ in items]

        enhanced_images = [it[0] for it in items]
        source_models = [it[1] for it in items]

        with self._lock:
            det_results = self._model.predict(
                enhanced_images,
                conf=self.confidence,
                imgsz=self.image_size,
                device=self.device,
                verbose=False,
            )

        batch_detections: List[List[Dict]] = []
        for result, pil_img, src_model in zip(det_results, enhanced_images, source_models):
            img_w, img_h = pil_img.size
            img_area = max(1, img_w * img_h)
            crop_dets: List[Dict] = []

            boxes = getattr(result, "boxes", None)
            if boxes is not None and len(boxes) > 0:
                for idx in range(len(boxes)):
                    raw_box = boxes[idx].xyxy[0].cpu().numpy()
                    label_idx = int(boxes[idx].cls[0])
                    raw_label = str(result.names[label_idx]).lower()
                    label = normalize_violation_label(raw_label)

                    if not label:
                        continue

                    if label == "incorrect-mask" and RESTAURANT_PPE_PREFER_NO_MASK_LABEL:
                        label = "no-mask"

                    score = float(boxes[idx].conf[0])
                    logger.info(
                        "PPE raw detection: raw_label='%s', norm_label='%s', score=%.3f, threshold=%.3f",
                        raw_label,
                        label,
                        score,
                        self.confidence,
                    )
                    if score < self.confidence:
                        continue

                    if RESTAURANT_PPE_ENABLE_OVERSIZE_FILTER and label in {
                        "no-hairnet",
                        "no-mask",
                        "incorrect-mask",
                    }:
                        bw = max(0, int(raw_box[2]) - int(raw_box[0]))
                        bh = max(0, int(raw_box[3]) - int(raw_box[1]))
                        if (bw * bh) / float(img_area) > RESTAURANT_PPE_MAX_HEADBOX_AREA_RATIO:
                            logger.info(
                                "Suppressed '%s' due to oversized box (ratio=%.2f > %.2f)",
                                label,
                                (bw * bh) / float(img_area),
                                RESTAURANT_PPE_MAX_HEADBOX_AREA_RATIO,
                            )
                            continue

                        # Anatomical vertical filter for head PPE: center must not be in lower 35% of person crop
                        cy = (int(raw_box[1]) + int(raw_box[3])) / 2.0
                        if cy > float(img_h) * 0.65:
                            logger.info(
                                "Suppressed '%s' due to invalid vertical position on person crop (cy/h=%.2f > 0.65)",
                                label,
                                cy / float(img_h),
                            )
                            continue

                    crop_dets.append(
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
                            "source_model": src_model,
                            "model_family": "restaurant-ppe",
                        }
                    )
            batch_detections.append(crop_dets)

        return batch_detections

    def predict_batch(
        self, pil_images: List[Image.Image], source_model: str
    ) -> List[List[Dict]]:
        """
        P0 Optimization #2 & #3: Dynamic Batched inference for multiple person crops across cameras.
        Ultralytics YOLO natively batches a list of PIL Images into a single
        GPU/NPU forward pass, while DynamicBatchScheduler coalesces concurrent
        crop requests across camera threads without mutex lock contention.
        """
        if not self._model or not pil_images:
            return [[] for _ in pil_images]

        # Low-light enhancement (CLAHE + conditional gamma) per crop if enabled
        enhanced_images: List[Image.Image] = []
        for img in pil_images:
            if config.RESTAURANT_PPE_ENHANCE_LOWLIGHT:
                enhanced_images.append(enhance_low_light(img))
            else:
                enhanced_images.append(img)

        items = [(img, source_model) for img in enhanced_images]

        # Dynamic Batch Scheduler path: coalesces across concurrent camera threads
        if self._batcher is not None and getattr(config, "DYNAMIC_BATCHING_ENABLED", True):
            try:
                return self._batcher.submit_batch(items)
            except Exception as e:
                logger.error(
                    "PPE dynamic batch scheduler failed; falling back to direct prediction: %s",
                    e,
                )

        return self._raw_batch_predict(items)

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
