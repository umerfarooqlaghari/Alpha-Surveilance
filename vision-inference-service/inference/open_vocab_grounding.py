"""
Experimental open-vocabulary grounding adapter.

This integration path is intended for Locate-Anything style models. The camera
rule trigger labels are passed directly as candidate labels for each inference
call, so the model only searches for explicitly configured concepts.
"""

from __future__ import annotations

import logging
import threading
from typing import Dict, List

from PIL import Image

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
        resolved_reference = self._normalize_reference(model_reference)

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

    def predict(self, pil_image: Image.Image, *, source_model: str, candidate_labels: List[str]) -> List[Dict]:
        if not self._detector:
            return []

        normalized_candidates = [str(label).strip().lower() for label in candidate_labels if str(label).strip()]
        if not normalized_candidates:
            return []

        with self._lock:
            raw_results = self._detector(pil_image, candidate_labels=normalized_candidates)

        detections: List[Dict] = []
        for raw in raw_results or []:
            label = str(raw.get("label", "")).strip().lower()
            box = raw.get("box") or {}
            try:
                detections.append(
                    {
                        "label": label,
                        "score": float(raw.get("score", 0.0)),
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