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
from inference.open_vocab_grounding import OpenVocabGroundingDetector
from inference.restaurant_ppe import MODEL_IDS as RESTAURANT_PPE_MODEL_IDS
from inference.restaurant_ppe import RestaurantPpeDetector
from inference.pest_detector import MODEL_IDS as PEST_MODEL_IDS
from inference.pest_detector import PestDetector
from inference.model_loader import ensure_model_local

try:
    from ultralytics import YOLO, YOLOWorld

    HAS_ULTRALYTICS = True
except ImportError:
    YOLO = None
    YOLOWorld = None
    HAS_ULTRALYTICS = False

try:
    from inference_sdk import InferenceHTTPClient

    HAS_INFERENCE_SDK = True
except ImportError:
    InferenceHTTPClient = None
    HAS_INFERENCE_SDK = False
from transformers import pipeline

logger = logging.getLogger("vision-service.inference")


class InferenceEngine:
    def __init__(self):
        self._legacy_lock = threading.Lock()
        self._registry: Dict[str, object] = {}
        self._restaurant_detectors_by_path: Dict[str, object] = {}
        self._pest_detectors_by_path: Dict[str, object] = {}
        self._open_vocab_detectors_by_reference: Dict[str, object] = {}
        # P2 fix #5: per-camera motion-gate state.
        # camera_id -> {"thumb": np.ndarray (gray, MOTION_GATE_SAMPLE_SIZE^2),
        #               "persons": List[Dict]}
        self._motion_cache: Dict[str, Dict] = {}

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
            if not HAS_INFERENCE_SDK:
                logger.warning(
                    "inference_sdk is unavailable on this Python runtime. "
                    "Roboflow-backed model identifiers will be skipped."
                )
                self._roboflow_client = None
            else:
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

    def _default_local_model_path(self, model_id: str) -> str:
        safe = "".join(ch if ch.isalnum() or ch in ("-", "_") else "-" for ch in model_id)
        return f"/tmp/models/{safe}.pt"

    def _resolve_model_artifact(self, model_id: str, rule, fallback_local_path: str = None, fallback_s3_key: str = None):
        local_path = (
            getattr(rule, "model_local_path", None)
            or fallback_local_path
            or self._default_local_model_path(model_id)
        )
        download_url = getattr(rule, "model_download_url", None)
        s3_bucket = getattr(rule, "model_s3_bucket", None) or config.MODEL_S3_BUCKET
        s3_key = getattr(rule, "model_s3_key", None) or fallback_s3_key
        return local_path, download_url, s3_bucket, s3_key

    def _ensure_restaurant_model(self, model_id: str, rule) -> None:
        if model_id in self._registry:
            return

        local_path, download_url, s3_bucket, s3_key = self._resolve_model_artifact(
            model_id=model_id,
            rule=rule,
            fallback_local_path=config.RESTAURANT_PPE_MODEL_PATH,
            fallback_s3_key=config.MODEL_S3_KEY,
        )

        weights_path = ensure_model_local(
            local_path=local_path,
            bucket=s3_bucket,
            s3_key=s3_key,
            download_url=download_url,
        )

        detector = self._restaurant_detectors_by_path.get(weights_path)
        if detector is None:
            detector = RestaurantPpeDetector(
                model_id=model_id,
                weights_path=weights_path,
                yolo_cls=YOLO,
                device=self.device,
                confidence=config.MIN_CONFIDENCE_RESTAURANT_PPE,
                image_size=config.RESTAURANT_PPE_IMAGE_SIZE,
            )
            self._restaurant_detectors_by_path[weights_path] = detector

        if detector.available:
            self._registry[model_id] = detector
        else:
            logger.error(
                "Restaurant PPE detector unavailable for model '%s' at path '%s'.",
                model_id,
                weights_path,
            )

    def _ensure_pest_model(self, model_id: str, rule) -> None:
        if model_id in self._registry:
            return

        local_path, download_url, s3_bucket, s3_key = self._resolve_model_artifact(
            model_id=model_id,
            rule=rule,
            fallback_local_path=config.PEST_MODEL_PATH,
            fallback_s3_key=config.PEST_MODEL_S3_KEY,
        )

        weights_path = ensure_model_local(
            local_path=local_path,
            bucket=s3_bucket,
            s3_key=s3_key,
            download_url=download_url,
        )

        detector = self._pest_detectors_by_path.get(weights_path)
        if detector is None:
            detector = PestDetector(
                weights_path=weights_path,
                yolo_cls=YOLO,
                device=self.device,
                confidence=config.MIN_CONFIDENCE_PEST,
                image_size=config.PEST_MODEL_IMAGE_SIZE,
            )
            self._pest_detectors_by_path[weights_path] = detector

        if detector.available:
            self._registry[model_id] = detector
        else:
            logger.warning(
                "Pest detector unavailable for model '%s' at path '%s'.",
                model_id,
                weights_path,
            )

    def _ensure_open_vocab_model(self, model_id: str, rule) -> None:
        if model_id in self._registry:
            return

        model_reference = (
            getattr(rule, "model_local_path", None)
            or getattr(rule, "model_download_url", None)
            or config.LOCATE_ANYTHING_MODEL_REFERENCE
        )

        detector = self._open_vocab_detectors_by_reference.get(model_reference)
        if detector is None:
            detector = OpenVocabGroundingDetector(
                model_id=model_id,
                model_reference=model_reference,
                pipeline_factory=pipeline,
                device=self.device,
            )
            self._open_vocab_detectors_by_reference[model_reference] = detector

        if detector.available:
            self._registry[model_id] = detector
        else:
            logger.error(
                "Open-vocab detector unavailable for model '%s' using reference '%s'.",
                model_id,
                model_reference,
            )

    def run_inference(self, pil_image: Image.Image, active_rules: List, camera_id: str = None) -> List[Dict]:
        """
        Runs inference on the provided image based on active camera rules.

        For restaurant-ppe models we optionally run a person-crop pre-layer
        (RESTAURANT_PPE_PERSON_CROP). The person detector is invoked AT MOST
        ONCE per frame, even if multiple PPE rules are configured and even if
        ``human-detection-v1`` is itself a configured rule — the same boxes
        are reused for cropping AND emitted as person detections.

        When ``MOTION_GATE_ENABLED`` is true AND a ``camera_id`` is provided,
        we additionally reuse the previous frame's person_boxes when the
        current frame is visually almost identical (mean abs diff on a small
        thumbnail < ``MOTION_GATE_THRESHOLD``). This skips the YOLOv11n call
        on static scenes — PPE inference still runs on the cached crops so a
        new no_glove on a stationary hand is still caught.
        """
        results: List[Dict] = []

        # Filter out rules whose model has been disabled in the registry.
        # We use the first rule's model_status for each unique model_id
        # (all rules sharing the same model_identifier will have the same status).
        rules_by_model: Dict[str, list] = {}
        for rule in active_rules:
            rules_by_model.setdefault(rule.model_identifier, []).append(rule)

        disabled_models: set = set()
        for model_id, rules in rules_by_model.items():
            status = getattr(rules[0], "model_status", "Available")
            if status == "Disabled":
                logger.warning(
                    "Model '%s' is disabled in the registry — skipping %d rule(s) for this frame.",
                    model_id, len(rules),
                )
                disabled_models.add(model_id)

        unique_model_ids = {
            rule.model_identifier
            for rule in active_rules
            if rule.model_identifier not in disabled_models
        }

        gated_persons = self._maybe_gated_persons(pil_image, camera_id)

        # Lazy person detection cache, shared across all PPE crop calls and
        # any explicit ``human-detection-v1`` rule in this frame.
        person_cache: Dict = {"detected": False, "boxes": None}
        if gated_persons is not None:
            person_cache = {"detected": True, "boxes": gated_persons}

        def _persons() -> List[Dict]:
            if not person_cache["detected"]:
                person_cache["boxes"] = self._detect_persons(pil_image)
                person_cache["detected"] = True
                self._update_motion_cache(camera_id, pil_image, person_cache["boxes"])
            return person_cache["boxes"] or []

        for model_id in unique_model_ids:
            model_rules = rules_by_model.get(model_id, [])
            model_rule = model_rules[0] if model_rules else None

            if model_rule and model_id in RESTAURANT_PPE_MODEL_IDS and model_id not in self._registry:
                self._ensure_restaurant_model(model_id, model_rule)

            if model_rule and model_id in PEST_MODEL_IDS and model_id not in self._registry:
                self._ensure_pest_model(model_id, model_rule)

            if (
                model_rule
                and getattr(model_rule, "model_type", "") == "OpenVocabGrounding"
                and model_id not in self._registry
            ):
                self._ensure_open_vocab_model(model_id, model_rule)

            model = self._registry.get(model_id)

            if isinstance(model, RestaurantPpeDetector):
                if config.RESTAURANT_PPE_PERSON_CROP:
                    results.extend(
                        self._run_ppe_on_person_crops(model, pil_image, _persons(), model_id)
                    )
                else:
                    results.extend(model.predict(pil_image, source_model=model_id))
                continue

            # Pest detector — always full-frame, no person-crop gate
            if isinstance(model, PestDetector):
                results.extend(model.predict(pil_image, source_model=model_id))
                continue

            if isinstance(model, OpenVocabGroundingDetector):
                candidate_labels = sorted(
                    {
                        str(label).strip().lower()
                        for rule in model_rules
                        for label in getattr(rule, "trigger_labels", [])
                        if str(label).strip()
                    }
                )
                if not candidate_labels:
                    logger.warning(
                        "Open-vocab model '%s' has no trigger labels configured for this camera; skipping.",
                        model_id,
                    )
                    continue
                results.extend(
                    model.predict(
                        pil_image,
                        source_model=model_id,
                        candidate_labels=candidate_labels,
                    )
                )
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

            # If this rule is the person detector AND we already ran it for a
            # PPE crop pass on this frame, reuse the cached boxes instead of
            # re-running YOLO11n. ~50-100 ms saved per frame in mixed configs.
            if model_id == "human-detection-v1" and person_cache["detected"]:
                for pbox in (person_cache["boxes"] or []):
                    results.append(
                        {
                            "label": "person",
                            "score": pbox.get("score", 0.0),
                            "box": {k: pbox[k] for k in ("xmin", "ymin", "xmax", "ymax")},
                            "source_model": model_id,
                        }
                    )
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

    # ──────────────────────────────────────────────────────────────────────    # Motion gate
    # ───────────────────────────────────────────────────────────────────

    def _frame_thumbnail(self, pil_image: Image.Image) -> np.ndarray:
        """Downscale to a tiny grayscale thumbnail for cheap motion comparison."""
        size = max(32, int(config.MOTION_GATE_SAMPLE_SIZE))
        thumb = pil_image.convert("L").resize((size, size), Image.BILINEAR)
        return np.asarray(thumb, dtype=np.int16)

    def _maybe_gated_persons(self, pil_image: Image.Image, camera_id) -> List[Dict]:
        """Return cached person_boxes when motion is below threshold; else None."""
        if not (config.MOTION_GATE_ENABLED and camera_id):
            return None
        prev = self._motion_cache.get(camera_id)
        if not prev or prev.get("thumb") is None:
            return None
        try:
            curr = self._frame_thumbnail(pil_image)
            diff = float(np.mean(np.abs(curr - prev["thumb"])))
        except Exception:  # noqa: BLE001
            return None
        if diff >= config.MOTION_GATE_THRESHOLD:
            return None
        # Refresh thumb (slow drift would otherwise never re-trigger detection).
        prev["thumb"] = curr
        cached = prev.get("persons") or []
        # Return a shallow copy so downstream mutation can't corrupt the cache.
        return [dict(p) for p in cached]

    def _update_motion_cache(self, camera_id, pil_image: Image.Image, persons: List[Dict]) -> None:
        if not (config.MOTION_GATE_ENABLED and camera_id):
            return
        try:
            self._motion_cache[camera_id] = {
                "thumb": self._frame_thumbnail(pil_image),
                "persons": [dict(p) for p in (persons or [])],
            }
        except Exception:  # noqa: BLE001
            pass

    # ───────────────────────────────────────────────────────────────────    # Person-crop pre-layer helpers
    # ──────────────────────────────────────────────────────────────────────

    def _detect_persons(self, pil_image: Image.Image) -> List[Dict]:
        """
        Run YOLOv11n on the full frame and return only ``person`` boxes
        (COCO class 0). Returns ``[]`` on any failure so callers can fall back
        gracefully to full-frame PPE inference.
        """
        model = self._registry.get("human-detection-v1")
        if not (HAS_ULTRALYTICS and isinstance(model, (YOLO, YOLOWorld))):
            return []
        try:
            det_results = model.predict(
                pil_image,
                conf=config.PERSON_DETECTOR_CONFIDENCE,
                classes=[0],  # COCO 'person'
                device=self.device,
                verbose=False,
            )
            persons: List[Dict] = []
            for result in det_results:
                boxes = result.boxes
                for idx in range(len(boxes)):
                    xy = boxes[idx].xyxy[0].cpu().numpy()
                    persons.append(
                        {
                            "xmin": int(xy[0]),
                            "ymin": int(xy[1]),
                            "xmax": int(xy[2]),
                            "ymax": int(xy[3]),
                            "score": float(boxes[idx].conf[0]),
                        }
                    )
            return persons
        except Exception as e:  # noqa: BLE001
            logger.error("Person pre-detection failed: %s", e)
            return []

    def _run_ppe_on_person_crops(
        self,
        ppe_model: RestaurantPpeDetector,
        pil_image: Image.Image,
        person_boxes: List[Dict],
        source_model: str,
    ) -> List[Dict]:
        """
        Crop each person bbox (with padding), run PPE on the crop, and offset
        the resulting detections back to full-frame coordinates. Each detection
        is tagged with the originating ``person_box`` so downstream rules
        (geofence, dwell) can anchor on the person rather than the small PPE
        feature box.

        No-person gate: if the person detector finds nobody, PPE inference is
        skipped entirely. This prevents false positives on empty scenes, pest
        images, or any frame with no visible human. If a real violation is
        missed because the person detector failed, lower PERSON_DETECTOR_CONFIDENCE
        (default 0.25) rather than re-enabling the full-frame fallback.

        Note on CLAHE (audit issue #3): when ``RESTAURANT_PPE_ENHANCE_LOWLIGHT``
        is true, the detector applies CLAHE + conditional gamma per CROP, not
        per frame. The gamma branch self-skips when mean L > 90, so each crop
        is normalised against ITS OWN luminance — a brightly lit person and a
        dimly lit one in the same frame will be enhanced independently. This
        is intentional (matches per-subject lighting better than a global
        normalisation) but means side-by-side debug visualisations will look
        inconsistent. Do not change without re-testing CAM-002 night scenes.
        """
        if not person_boxes:
            logger.debug("[%s] No persons detected — skipping PPE inference.", source_model)
            return []

        W, H = pil_image.size
        pad = max(0.0, config.PERSON_CROP_PADDING)
        all_dets: List[Dict] = []

        for pbox in person_boxes:
            bw = pbox["xmax"] - pbox["xmin"]
            bh = pbox["ymax"] - pbox["ymin"]
            if bw <= 0 or bh <= 0:
                continue
            x1 = max(0, int(pbox["xmin"] - pad * bw))
            y1 = max(0, int(pbox["ymin"] - pad * bh))
            x2 = min(W, int(pbox["xmax"] + pad * bw))
            y2 = min(H, int(pbox["ymax"] + pad * bh))
            if x2 - x1 < 32 or y2 - y1 < 32:
                # Crop too small to feed YOLO meaningfully; skip.
                continue

            crop = pil_image.crop((x1, y1, x2, y2))
            try:
                crop_dets = ppe_model.predict(crop, source_model=source_model)
            except Exception as e:  # noqa: BLE001
                logger.error("PPE inference on person crop failed: %s", e)
                continue

            for d in crop_dets:
                b = d["box"]
                b["xmin"] += x1
                b["ymin"] += y1
                b["xmax"] += x1
                b["ymax"] += y1
                d["person_box"] = {
                    "xmin": pbox["xmin"],
                    "ymin": pbox["ymin"],
                    "xmax": pbox["xmax"],
                    "ymax": pbox["ymax"],
                }
                d["person_score"] = pbox.get("score")
            all_dets.extend(crop_dets)

        return self._nms_dets(all_dets, iou_threshold=0.45)

    @staticmethod
    def _iou(a: Dict, b: Dict) -> float:
        """Intersection-over-Union for two box dicts with xmin/ymin/xmax/ymax."""
        ax1, ay1, ax2, ay2 = a["xmin"], a["ymin"], a["xmax"], a["ymax"]
        bx1, by1, bx2, by2 = b["xmin"], b["ymin"], b["xmax"], b["ymax"]
        ix1, iy1 = max(ax1, bx1), max(ay1, by1)
        ix2, iy2 = min(ax2, bx2), min(ay2, by2)
        inter = max(0, ix2 - ix1) * max(0, iy2 - iy1)
        if inter == 0:
            return 0.0
        area_a = max(0, ax2 - ax1) * max(0, ay2 - ay1)
        area_b = max(0, bx2 - bx1) * max(0, by2 - by1)
        union = area_a + area_b - inter
        return inter / union if union > 0 else 0.0

    def _nms_dets(self, dets: List[Dict], iou_threshold: float = 0.45) -> List[Dict]:
        """
        Cross-crop NMS: when multiple person crops overlap, the same PPE feature
        (e.g. a hairnet) can appear in several crops and produce near-identical
        boxes in full-frame coordinates. This pass suppresses lower-confidence
        duplicates using standard greedy IoU NMS, applied per label class.
        """
        if len(dets) <= 1:
            return dets

        # Group by label so we only compare same-class boxes
        by_label: Dict[str, List] = {}
        for i, d in enumerate(dets):
            lbl = d.get("label", "")
            by_label.setdefault(lbl, []).append((i, d))

        keep_indices = set()
        for lbl, items in by_label.items():
            # Sort descending by score
            items.sort(key=lambda x: x[1].get("score", 0), reverse=True)
            suppressed = set()
            for i, (idx_i, det_i) in enumerate(items):
                if idx_i in suppressed:
                    continue
                keep_indices.add(idx_i)
                for idx_j, det_j in items[i + 1:]:
                    if idx_j in suppressed:
                        continue
                    if self._iou(det_i["box"], det_j["box"]) >= iou_threshold:
                        suppressed.add(idx_j)

        return [d for i, d in enumerate(dets) if i in keep_indices]

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
