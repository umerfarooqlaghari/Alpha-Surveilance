"""
Experimental open-vocabulary grounding adapter.

This integration path is intended for Locate-Anything style models. The camera
rule trigger labels are passed directly as candidate labels for each inference
call, so the model only searches for explicitly configured concepts.
"""

from __future__ import annotations

import logging
import threading
from typing import Dict, List, Optional, Union
import numpy as np

from PIL import Image

import config

try:
    from ultralytics import YOLO  # type: ignore
    try:
        from ultralytics import YOLOE  # type: ignore
    except ImportError:
        YOLOE = YOLO
    HAS_ULTRALYTICS = True
except (ImportError, ModuleNotFoundError):
    YOLO = None
    YOLOE = None
    HAS_ULTRALYTICS = False

logger = logging.getLogger("vision-service.inference.open-vocab")


class OpenVocabGroundingDetector:
    def __init__(
        self,
        *,
        model_id: str,
        model_reference: str,
        pipeline_factory,
        device: str,
    ):
        self.model_id = model_id
        self.model_reference = model_reference
        self._lock = threading.Lock()
        self._detector = None
        self._is_ultralytics = False
        resolved_reference = self._normalize_reference(model_reference)

        # Check if model reference points to an Ultralytics .pt model (e.g. yoloe-26m-seg.pt)
        if resolved_reference.endswith(".pt") or "yolo" in resolved_reference.lower():
            if HAS_ULTRALYTICS:
                try:
                    loader = YOLOE if YOLOE is not None else YOLO
                    self._detector = loader(resolved_reference)
                    self._is_ultralytics = True
                    logger.info(
                        "Loaded Ultralytics YOLOE open-vocab model '%s' from %s",
                        model_id,
                        resolved_reference,
                    )
                except Exception as e:
                    logger.error(
                        "Failed to load Ultralytics YOLOE model '%s' from %s: %s",
                        model_id,
                        resolved_reference,
                        e,
                    )
            else:
                logger.error(
                    "Ultralytics package not available to load open-vocab model '%s'",
                    model_id,
                )

        if not self._detector and pipeline_factory is not None:
            try:
                pipeline_device = 0 if device == "cuda" else -1
                self._detector = pipeline_factory(
                    task="zero-shot-object-detection",
                    model=resolved_reference,
                    device=pipeline_device,
                )
                logger.info(
                    "Loaded open-vocab grounding model '%s' from %s",
                    model_id,
                    model_reference,
                )
            except Exception as e:  # noqa: BLE001
                logger.error(
                    "Failed to load open-vocab grounding model '%s' from %s: %s",
                    model_id,
                    model_reference,
                    e,
                )

    @property
    def available(self) -> bool:
        return self._detector is not None

    @staticmethod
    def _normalize_reference(model_reference: str) -> str:
        reference = str(model_reference or "").strip()
        if reference.startswith("hf://"):
            return reference[5:]
        return reference

    def predict(
        self,
        image: Union[Image.Image, np.ndarray],
        *,
        source_model: str,
        candidate_labels: List[str],
        min_confidence: Optional[float] = None,
    ) -> List[Dict]:
        if not self._detector or image is None:
            return []

        if min_confidence is None:
            min_confidence = getattr(config, "MIN_CONFIDENCE_OPEN_VOCAB", 0.80)

        normalized_candidates = [str(label).strip().lower() for label in candidate_labels if str(label).strip()]
        if not normalized_candidates:
            return []

        if self._is_ultralytics:
            with self._lock:
                try:
                    if hasattr(self._detector, "set_classes"):
                        self._detector.set_classes(normalized_candidates)
                    # Ultralytics natively handles BGR numpy array or PIL Image
                    preds = self._detector.predict(image, conf=min_confidence, verbose=False)
                except Exception as pred_err:
                    logger.error("Error during Ultralytics open-vocab predict on '%s': %s", self.model_id, pred_err)
                    return []

            detections: List[Dict] = []
            for pred in preds or []:
                if pred.boxes is None or len(pred.boxes) == 0:
                    continue
                boxes = pred.boxes.xyxy.cpu().numpy()
                confs = pred.boxes.conf.cpu().numpy()
                clses = pred.boxes.cls.cpu().numpy()
                names = getattr(pred, "names", {})
                for i in range(len(boxes)):
                    score = float(confs[i])
                    if score < min_confidence:
                        continue
                    cls_idx = int(clses[i])
                    label_name = names.get(cls_idx, str(cls_idx))
                    detections.append(
                        {
                            "label": str(label_name).strip().lower(),
                            "score": score,
                            "box": {
                                "xmin": int(boxes[i][0]),
                                "ymin": int(boxes[i][1]),
                                "xmax": int(boxes[i][2]),
                                "ymax": int(boxes[i][3]),
                            },
                            "source_model": source_model,
                            "model_family": "open-vocab-grounding",
                        }
                    )
            return detections

        # HuggingFace pipeline path: Strictly enforce RGB color contract
        if isinstance(image, np.ndarray):
            import cv2
            rgb_arr = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
            hf_input = Image.fromarray(rgb_arr)
        else:
            hf_input = image

        with self._lock:
            raw_results = self._detector(hf_input, candidate_labels=normalized_candidates)

        detections: List[Dict] = []
        for raw in raw_results or []:
            label = str(raw.get("label", "")).strip().lower()
            box = raw.get("box") or {}
            score = float(raw.get("score", 0.0))
            if score < min_confidence:
                continue
            try:
                detections.append(
                    {
                        "label": label,
                        "score": score,
                        "box": {
                            "xmin": int(box.get("xmin", 0)),
                            "ymin": int(box.get("ymin", 0)),
                            "xmax": int(box.get("xmax", 0)),
                            "ymax": int(box.get("ymax", 0)),
                        },
                        "source_model": source_model,
                        "model_family": "open-vocab-grounding",
                    }
                )
            except Exception as e:  # noqa: BLE001
                logger.warning("Skipping malformed open-vocab detection %s: %s", raw, e)

        return detections