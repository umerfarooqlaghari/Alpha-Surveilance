"""
inference/inference_engine.py
Modularized inference execution engine.

Restaurant PPE compliance uses a fine-tuned YOLO model only. The previous
zero-shot hygiene fallback was intentionally removed because hairnet and mask
violations must come from trained model classes, not prompt-based guesses.
"""
from __future__ import annotations

import logging
import os
import threading
import time
import zlib
from typing import Any, Dict, List, Optional, Set, Tuple

import numpy as np
from PIL import Image

import config
from inference.model_routing import infer_rule_family
from inference.open_vocab_grounding import OpenVocabGroundingDetector
from inference.restaurant_ppe import RestaurantPpeDetector
from inference.pest_detector import PestDetector
from inference.model_loader import ensure_model_local

try:
    import torch  # type: ignore
    HAS_TORCH = True
except (ImportError, ModuleNotFoundError):
    torch = None  # type: ignore
    HAS_TORCH = False

try:
    from ultralytics import YOLO, YOLOWorld  # type: ignore
    HAS_ULTRALYTICS = True
except (ImportError, ModuleNotFoundError):
    YOLO = None  # type: ignore
    YOLOWorld = None  # type: ignore
    HAS_ULTRALYTICS = False

try:
    from inference_sdk import InferenceHTTPClient  # type: ignore
    HAS_INFERENCE_SDK = True
except (ImportError, ModuleNotFoundError):
    InferenceHTTPClient = None  # type: ignore
    HAS_INFERENCE_SDK = False

try:
    from transformers import pipeline  # type: ignore
    HAS_TRANSFORMERS = True
except (ImportError, ModuleNotFoundError):
    pipeline = None  # type: ignore
    HAS_TRANSFORMERS = False

logger = logging.getLogger("vision-service.inference")


class InferenceEngine:
    def __init__(self):
        self._legacy_lock = threading.Lock()
        self._model_load_lock = threading.Lock()
        self._registry: Dict[str, object] = {}
        # Tracks the resolved artifact signature (path/source/confidence/etc.)
        # last used to build the currently-registered detector for a given
        # model_id. Cheap to compare every frame; only triggers a real reload
        # (file check / possible download / detector construction) when the
        # signature actually changes — e.g. after an admin updates the AI
        # Model Library row for that model_identifier.
        self._model_signatures: Dict[str, tuple] = {}
        self._restaurant_detectors_by_path: Dict[str, object] = {}
        self._pest_detectors_by_path: Dict[str, object] = {}
        self._open_vocab_detectors_by_reference: Dict[str, object] = {}
        # P2 fix #5: per-camera motion-gate state.
        # camera_id -> {"thumb": np.ndarray (gray, MOTION_GATE_SAMPLE_SIZE^2),
        #               "persons": List[Dict]}
        self._motion_cache: Dict[str, Dict] = {}
        # P1 Optimization #4: Per-camera, per-model cadence state for interleaved sub-sampling.
        # camera_id -> model_id -> {"last_run_time": float, "frame_count": int, "cached_results": List[Dict]}
        self._model_cadence_state: Dict[str, Dict[str, Dict[str, Any]]] = {}
        self._cadence_lock = threading.Lock()

        # V6 fix: ultralytics YOLO .predict() is NOT thread-safe — the model
        # object carries mutable predictor state, so concurrent calls from
        # multiple camera capture threads corrupt results or segfault. Each
        # shared model instance gets exactly one lock (keyed by id(model)),
        # mirroring the pattern already used in RestaurantPpeDetector.
        self._predict_locks: Dict[int, threading.Lock] = {}
        self._predict_locks_guard = threading.Lock()

        # V7 fix: MPS allocator hygiene — variable-shaped person crops make
        # torch's MPS cache grow without bound. We periodically release the
        # cache (see _maybe_release_mps_cache) instead of letterboxing crops,
        # because letterboxing would change detection coordinates and recall
        # characteristics (riskier). Counter is intentionally approximate;
        # racy increments across capture threads are harmless.
        self._mps_frames_since_cache_release = 0

        forced = str(getattr(config, "FORCE_DEVICE", "") or "").strip().lower()
        mps_available = bool(HAS_TORCH and hasattr(torch, "backends") and hasattr(torch.backends, "mps") and torch.backends.mps.is_available())
        cuda_available = bool(HAS_TORCH and hasattr(torch, "cuda") and torch.cuda.is_available())

        if forced in ("cpu", "cuda", "mps"):
            available = (
                forced == "cpu"
                or (forced == "mps" and mps_available)
                or (forced == "cuda" and cuda_available)
            )
            if available:
                self.device = forced
                logger.info("FORCE_DEVICE=%s — device selection overridden.", forced)
            else:
                self.device = "cpu"
                logger.warning(
                    "FORCE_DEVICE=%s requested but that backend is unavailable; using CPU.",
                    forced,
                )
        elif mps_available:
            self.device = "mps"
            logger.info("Apple Silicon MPS acceleration available.")
        elif cuda_available:
            self.device = "cuda"
            logger.info("NVIDIA CUDA acceleration available.")
        else:
            self.device = "cpu"
            logger.info("Using CPU for inference.")

        self._person_batcher = None
        self._load_models()
        self._init_dynamic_batchers()

    def _init_dynamic_batchers(self) -> None:
        """P0 Optimization #3: Central Dynamic Batch Scheduler for shared person detection."""
        if not getattr(config, "DYNAMIC_BATCHING_ENABLED", True):
            return

        model = self._registry.get("human-detection-v1")
        if HAS_ULTRALYTICS and isinstance(model, (YOLO, YOLOWorld)):
            if self._person_batcher is not None:
                self._person_batcher.stop()
            try:
                from inference.dynamic_batcher import DynamicBatchScheduler

                def _batch_detect_persons(batch_images: List[Image.Image]) -> List[List[Dict]]:
                    with self._predict_lock(model):
                        det_results = model.predict(
                            batch_images,
                            conf=config.PERSON_DETECTOR_CONFIDENCE,
                            classes=[0],  # COCO 'person'
                            device=self.device,
                            verbose=False,
                        )
                    batch_persons: List[List[Dict]] = []
                    for result in det_results:
                        persons: List[Dict] = []
                        boxes = getattr(result, "boxes", None)
                        if boxes is not None and len(boxes) > 0:
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
                        batch_persons.append(persons)
                    return batch_persons

                self._person_batcher = DynamicBatchScheduler(
                    _batch_detect_persons,
                    name="person-detector",
                    max_batch_size=getattr(config, "DYNAMIC_BATCH_MAX_SIZE", 8),
                    max_wait_ms=getattr(config, "DYNAMIC_BATCH_WAIT_MS", 5.0),
                )
            except Exception as e:
                logger.warning("Could not initialize DynamicBatchScheduler for person detector: %s", e)
                self._person_batcher = None

    def _is_heavy_model(self, model_id: str, rule_family: str, model_rules: list) -> bool:
        """
        P1 Optimization #4: Identifies whether a model belongs to a heavy
        inference family (pose estimation, open-vocabulary grounding, pest detection)
        that can be sub-sampled/interleaved across frames to prevent latency accumulation.
        """
        if rule_family in ("pose-estimation", "open-vocab", "pest-detection"):
            return True
        mid = str(model_id).lower()
        if any(h in mid for h in ("pose", "keypoint", "yoloe", "openvocab", "grounding", "pest")):
            return True
        return False

    def _should_run_cadence(
        self,
        camera_id: Optional[str],
        model_id: str,
        rule_family: str,
        model_rules: list,
    ) -> Tuple[bool, Optional[List[Dict]]]:
        """
        Determine if the model should execute on the current frame under interleaved sub-sampling.
        Returns:
            (should_execute: bool, cached_detections_to_emit: Optional[List[Dict]])
        """
        if not camera_id or not getattr(config, "INTERLEAVED_SAMPLING_ENABLED", True):
            return True, None

        if not self._is_heavy_model(model_id, rule_family, model_rules):
            return True, None

        stride = max(1, int(getattr(config, "HEAVY_MODEL_FRAME_INTERVAL", 3)))
        if stride <= 1:
            return True, None

        max_cadence_sec = max(0.01, float(getattr(config, "HEAVY_MODEL_MAX_CADENCE_SECONDS", 1.5)))
        now = time.monotonic()

        lock = getattr(self, "_cadence_lock", None)
        if lock is None:
            self._cadence_lock = threading.Lock()
            lock = self._cadence_lock
        if not hasattr(self, "_model_cadence_state"):
            self._model_cadence_state = {}

        with lock:
            cam_state = self._model_cadence_state.setdefault(camera_id, {})
            m_state = cam_state.setdefault(
                model_id,
                {"frame_count": -1, "last_run_time": 0.0, "cached_results": []},
            )
            m_state["frame_count"] += 1
            frame_count = m_state["frame_count"]
            time_since_last = (
                now - m_state["last_run_time"] if m_state["last_run_time"] > 0 else float("inf")
            )

            if frame_count == 0:
                # Always run on the very first frame for this model/camera to seed detections immediately
                should_run = True
            else:
                # Stagger different heavy models by deterministic CRC32 hash so they don't collide
                offset = zlib.crc32(model_id.encode("utf-8")) % stride
                should_run = (frame_count % stride == offset) or (time_since_last >= max_cadence_sec)

            if should_run:
                m_state["last_run_time"] = now
                return True, None
            else:
                cached = m_state.get("cached_results") or []
                copied_results = []
                for d in cached:
                    cd = dict(d)
                    if isinstance(d.get("box"), dict):
                        cd["box"] = dict(d["box"])
                    if isinstance(d.get("person_box"), dict):
                        cd["person_box"] = dict(d["person_box"])
                    cd["interleaved_cached"] = True
                    copied_results.append(cd)
                return False, copied_results

    def _update_cadence_cache(self, camera_id: Optional[str], model_id: str, new_detections: List[Dict]) -> None:
        """Update cached detections for the model after a successful forward pass."""
        if not camera_id or not getattr(config, "INTERLEAVED_SAMPLING_ENABLED", True):
            return
        lock = getattr(self, "_cadence_lock", None)
        if lock is None:
            self._cadence_lock = threading.Lock()
            lock = self._cadence_lock
        if not hasattr(self, "_model_cadence_state"):
            self._model_cadence_state = {}
        with lock:
            cam_state = self._model_cadence_state.setdefault(camera_id, {})
            m_state = cam_state.setdefault(
                model_id,
                {"frame_count": 0, "last_run_time": time.monotonic(), "cached_results": []},
            )
            m_state["cached_results"] = new_detections

    def clear_cadence_cache(self, camera_id: Optional[str] = None) -> None:
        """Clear interleaved cadence cache for a camera or all cameras."""
        lock = getattr(self, "_cadence_lock", None)
        if lock is None:
            return
        with lock:
            if hasattr(self, "_model_cadence_state"):
                if camera_id:
                    self._model_cadence_state.pop(camera_id, None)
                else:
                    self._model_cadence_state.clear()

    def _predict_lock(self, model) -> threading.Lock:
        """Return the per-model lock guarding non-thread-safe predict() calls."""
        with self._predict_locks_guard:
            key = id(model)
            lock = self._predict_locks.get(key)
            if lock is None:
                lock = threading.Lock()
                self._predict_locks[key] = lock
            return lock

    def _maybe_release_mps_cache(self) -> None:
        """Periodically return PyTorch MPS allocator memory to the OS.

        Person crops vary in size across frames, causing PyTorch's MPS allocator
        to hold many small tensors in its pool. Calling empty_cache() every N
        frames releases them without changing frame processing semantics. We do
        NOT letterbox crops to fixed size because that changes detection recall
        and shifts bounding box coordinates (which we map back onto full-frame
        edges) — too risky without a re-validation pass. empty_cache() is a
        pure allocator operation with no effect on results.
        """
        if self.device != "mps" or not HAS_TORCH:
            return
        every_n = int(getattr(config, "MPS_EMPTY_CACHE_EVERY_N_FRAMES", 50))
        if every_n <= 0:
            return
        self._mps_frames_since_cache_release += 1
        if self._mps_frames_since_cache_release >= every_n:
            self._mps_frames_since_cache_release = 0
            try:
                if hasattr(torch, "mps") and hasattr(torch.mps, "empty_cache"):
                    torch.mps.empty_cache()
            except Exception:  # noqa: BLE001
                logger.debug("torch.mps.empty_cache() failed (non-fatal)", exc_info=True)

    def _model_confidence(self, rule, family: str | None = None) -> float:
        configured = getattr(rule, "model_min_confidence", None)
        if configured is not None:
            return float(configured)

        if family == "restaurant-ppe":
            return config.MIN_CONFIDENCE_RESTAURANT_PPE
        if family == "pest-detection":
            return config.MIN_CONFIDENCE_PEST
        if family in ("open-vocab", "open-vocab-grounding"):
            return getattr(config, "MIN_CONFIDENCE_OPEN_VOCAB", 0.80)
        if getattr(rule, "model_identifier", "") == "human-detection-v1":
            return config.MIN_CONFIDENCE_HUGGINGFACE
        return config.MIN_CONFIDENCE_ROBOFLOW

    def _model_image_size(self, rule, family: str | None = None) -> int:
        configured = getattr(rule, "model_image_size", None)
        if configured is not None and configured > 0:
            return int(configured)
        return config.DEFAULT_MODEL_IMAGE_SIZE

    def _load_models(self):
        logger.info("Loading object detection models into registry...")

        if HAS_ULTRALYTICS:
            try:
                import os

                os.makedirs(config.MODEL_CACHE_DIR, exist_ok=True)

                configured = getattr(config, "HUMAN_DETECTION_MODEL_WEIGHTS", "yolo11n.pt")
                if os.path.isabs(configured) and os.path.exists(configured):
                    yolo_weights = configured
                else:
                    filename = os.path.basename(configured)
                    yolo_weights = os.path.join(config.MODEL_CACHE_DIR, filename)
                    if not os.path.exists(yolo_weights):
                        for candidate in (
                            os.path.join("/tmp/models", filename),
                            os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), filename),
                            os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), filename),
                            f"/app/{filename}",
                            configured,
                        ):
                            if os.path.exists(candidate):
                                yolo_weights = candidate
                                break
                        else:
                            try:
                                s3_path = ensure_model_local(
                                    local_path=os.path.join("/tmp/models", filename),
                                    bucket=config.S3_BUCKET_NAME,
                                    s3_key=f"models/{filename}",
                                )
                                if os.path.exists(s3_path):
                                    yolo_weights = s3_path
                                else:
                                    yolo_weights = filename
                            except Exception as dl_err:
                                logger.warning("Could not auto-fetch %s from S3: %s", filename, dl_err)
                                yolo_weights = filename

                logger.info("Loading person detector from %s", yolo_weights)
                det_model = YOLO(yolo_weights)
                self._registry["human-detection-v1"] = det_model
                self._registry["yolo26m"] = det_model


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
        os.makedirs(config.MODEL_CACHE_DIR, exist_ok=True)
        return os.path.join(config.MODEL_CACHE_DIR, f"{safe}.pt")

    def _resolve_model_artifact(self, model_id: str, rule, fallback_local_path: str = None, fallback_s3_key: str = None):
        local_path = (
            getattr(rule, "model_local_path", None)
            or fallback_local_path
            or self._default_local_model_path(model_id)
        )
        download_url = getattr(rule, "model_download_url", None)
        s3_bucket = getattr(rule, "model_s3_bucket", None)
        s3_key = getattr(rule, "model_s3_key", None) or fallback_s3_key
        return local_path, download_url, s3_bucket, s3_key

    def _ensure_restaurant_model(self, model_id: str, rule) -> None:
        local_path, download_url, s3_bucket, s3_key = self._resolve_model_artifact(
            model_id=model_id,
            rule=rule,
        )
        confidence = self._model_confidence(rule, "restaurant-ppe")
        image_size = self._model_image_size(rule, "restaurant-ppe")
        expected_sha256 = getattr(rule, "model_sha256", None)
        signature = (local_path, download_url, s3_bucket, s3_key, expected_sha256, confidence, image_size)
        if model_id in self._registry and (
            self._model_signatures.get(model_id) == signature
            or not HAS_ULTRALYTICS
            or not any((getattr(rule, "model_local_path", None), getattr(rule, "model_download_url", None), getattr(rule, "model_s3_bucket", None), getattr(rule, "model_s3_key", None)))
        ):
            return

        with self._model_load_lock:
            if model_id in self._registry and (
                self._model_signatures.get(model_id) == signature
                or not HAS_ULTRALYTICS
            ):
                return

            weights_path = ensure_model_local(
                local_path=local_path,
                bucket=s3_bucket,
                s3_key=s3_key,
                download_url=download_url,
                expected_sha256=expected_sha256,
            )

            cache_key = (weights_path, confidence)
            detector = self._restaurant_detectors_by_path.get(cache_key)
            if detector is None:
                detector = RestaurantPpeDetector(
                    model_id=model_id,
                    weights_path=weights_path,
                    yolo_cls=YOLO,
                    device=self.device,
                    confidence=confidence,
                    image_size=image_size,
                )
                self._restaurant_detectors_by_path[cache_key] = detector

            if detector.available:
                reloaded = model_id in self._registry
                self._registry[model_id] = detector
                self._model_signatures[model_id] = signature
                if reloaded:
                    logger.info(
                        "Reloaded restaurant PPE model '%s' — AI Model Library artifact changed (path=%s).",
                        model_id, weights_path,
                    )
            else:
                logger.error(
                    "Restaurant PPE detector unavailable for model '%s' at path '%s'.",
                    model_id,
                    weights_path,
                )

    def _ensure_pest_model(self, model_id: str, rule) -> None:
        local_path, download_url, s3_bucket, s3_key = self._resolve_model_artifact(
            model_id=model_id,
            rule=rule,
        )
        confidence = self._model_confidence(rule, "pest-detection")
        image_size = self._model_image_size(rule, "pest-detection")
        expected_sha256 = getattr(rule, "model_sha256", None)
        signature = (local_path, download_url, s3_bucket, s3_key, expected_sha256, confidence, image_size)

        if model_id in self._registry and (
            self._model_signatures.get(model_id) == signature
            or not HAS_ULTRALYTICS
            or not any((getattr(rule, "model_local_path", None), getattr(rule, "model_download_url", None), getattr(rule, "model_s3_bucket", None), getattr(rule, "model_s3_key", None)))
        ):
            return

        with self._model_load_lock:
            if model_id in self._registry and (
                self._model_signatures.get(model_id) == signature
                or not HAS_ULTRALYTICS
            ):
                return

            weights_path = ensure_model_local(
                local_path=local_path,
                bucket=s3_bucket,
                s3_key=s3_key,
                download_url=download_url,
                # M23 fix: see restaurant-ppe equivalent above.
                expected_sha256=expected_sha256,
            )

            cache_key = (weights_path, confidence)
            detector = self._pest_detectors_by_path.get(cache_key)
            if detector is None:
                detector = PestDetector(
                    weights_path=weights_path,
                    yolo_cls=YOLO,
                    device=self.device,
                    confidence=confidence,
                    image_size=image_size,
                )
                self._pest_detectors_by_path[cache_key] = detector

            if detector.available:
                reloaded = model_id in self._registry
                self._registry[model_id] = detector
                self._model_signatures[model_id] = signature
                if reloaded:
                    logger.info(
                        "Reloaded pest model '%s' — AI Model Library artifact changed (path=%s).",
                        model_id, weights_path,
                    )
            else:
                logger.warning(
                    "Pest detector unavailable for model '%s' at path '%s'.",
                    model_id,
                    weights_path,
                )

    def _ensure_open_vocab_model(self, model_id: str, rule) -> None:
        local_path, download_url, s3_bucket, s3_key = self._resolve_model_artifact(
            model_id=model_id,
            rule=rule,
            fallback_local_path="/tmp/models/yoloe-26m-seg.pt",
            fallback_s3_key="models/yoloe-26m-seg.pt",
        )
        expected_sha256 = getattr(rule, "model_sha256", None)

        if (local_path and local_path.endswith(".pt")) or (s3_key and s3_key.endswith(".pt")) or (download_url and download_url.endswith(".pt")):
            weights_path = ensure_model_local(
                local_path=local_path,
                bucket=s3_bucket or config.S3_BUCKET_NAME,
                s3_key=s3_key,
                download_url=download_url,
                expected_sha256=expected_sha256,
            )
            model_reference = weights_path
        else:
            model_reference = (
                getattr(rule, "model_local_path", None)
                or getattr(rule, "model_download_url", None)
                or config.LOCATE_ANYTHING_MODEL_REFERENCE
            )

        signature = (model_reference,)

        if model_id in self._registry and self._model_signatures.get(model_id) == signature:
            return

        with self._model_load_lock:
            if model_id in self._registry and self._model_signatures.get(model_id) == signature:
                return

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
                reloaded = model_id in self._registry
                self._registry[model_id] = detector
                self._model_signatures[model_id] = signature
                if reloaded:
                    logger.info(
                        "Reloaded open-vocab model '%s' — reference changed (%s).",
                        model_id, model_reference,
                    )
            else:
                logger.error(
                    "Open-vocab detector unavailable for model '%s' using reference '%s'.",
                    model_id,
                    model_reference,
                )

    def _ensure_pose_model(self, model_id: str, rule) -> None:
        local_path, download_url, s3_bucket, s3_key = self._resolve_model_artifact(
            model_id=model_id,
            rule=rule,
            fallback_local_path="/tmp/models/yolo26m-pose.pt",
            fallback_s3_key="models/yolo26m-pose.pt",
        )
        confidence = self._model_confidence(rule)
        signature = (local_path, download_url, s3_bucket, s3_key, confidence)

        if model_id in self._registry and self._model_signatures.get(model_id) == signature:
            return

        with self._model_load_lock:
            if model_id in self._registry and self._model_signatures.get(model_id) == signature:
                return

            weights_path = ensure_model_local(
                local_path=local_path,
                bucket=s3_bucket,
                s3_key=s3_key,
                download_url=download_url or "https://github.com/ultralytics/assets/releases/download/v8.3.0/yolo11m-pose.pt",
            )
            if not os.path.exists(weights_path):
                weights_path = "yolo11n-pose.pt"

            try:
                if HAS_ULTRALYTICS:
                    pose_model = YOLO(weights_path)
                    self._registry[model_id] = pose_model
                    self._model_signatures[model_id] = signature
                    logger.info("Loaded YOLO pose model '%s' from %s", model_id, weights_path)
                else:
                    logger.error("Ultralytics not available to load pose model '%s'", model_id)
            except Exception as e:
                logger.error("Failed to load pose model '%s': %s", model_id, e)

    def _ensure_human_detector(self, model_id: str, rule) -> None:
        fallback_path = "/tmp/models/yolo26m.pt" if "26" in str(model_id) else "/tmp/models/yolo11n.pt"
        local_path, download_url, s3_bucket, s3_key = self._resolve_model_artifact(
            model_id=model_id,
            rule=rule,
            fallback_local_path=fallback_path,
            fallback_s3_key=f"models/{model_id}.pt",
        )
        expected_sha256 = getattr(rule, "model_sha256", None)
        signature = (local_path, download_url, s3_bucket, s3_key, expected_sha256)
        if model_id in self._registry and self._model_signatures.get(model_id) == signature:
            return

        with self._model_load_lock:
            if model_id in self._registry and self._model_signatures.get(model_id) == signature:
                return

            weights_path = ensure_model_local(
                local_path=local_path,
                bucket=s3_bucket or config.S3_BUCKET_NAME,
                s3_key=s3_key,
                download_url=download_url,
                expected_sha256=expected_sha256,
            )
            if not os.path.exists(weights_path):
                weights_path = getattr(config, "HUMAN_DETECTION_MODEL_WEIGHTS", "yolo11n.pt")

            try:
                if HAS_ULTRALYTICS:
                    det_model = YOLO(weights_path)
                    # Update both the primary human gating detector and this model_id
                    self._registry["human-detection-v1"] = det_model
                    self._registry[model_id] = det_model
                    self._model_signatures[model_id] = signature
                    self._model_signatures["human-detection-v1"] = signature
                    logger.info("Loaded custom/updated person detector '%s' from %s", model_id, weights_path)
            except Exception as e:
                logger.error("Failed to load custom person detector '%s': %s", model_id, e)

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
            # M9 fix: count motion-gate hits so dashboards can show the
            # % of frames that skipped the YOLOv11n call. Without this the
            # gauge stays at 0 and "motion gate effectiveness" panels are dead.
            try:
                import metrics as _vis_metrics
                _vis_metrics.motion_gate_hits_total.inc()
            except Exception:
                pass

        def _persons() -> List[Dict]:
            if not person_cache["detected"]:
                person_cache["boxes"] = self._detect_persons(pil_image)
                person_cache["detected"] = True
                self._update_motion_cache(camera_id, pil_image, person_cache["boxes"])
                # M9 fix: count actual person-detector invocations so we can
                # plot real YOLOv11n load (separate from motion-gate hits).
                try:
                    import metrics as _vis_metrics
                    _vis_metrics.person_detector_runs_total.inc()
                except Exception:
                    pass
            return person_cache["boxes"] or []

        for model_id in unique_model_ids:
            model_rules = rules_by_model.get(model_id, [])
            model_rule = model_rules[0] if model_rules else None
            rule_family = infer_rule_family(
                getattr(model_rule, "model_type", "") if model_rule else "",
                getattr(model_rule, "trigger_labels", []) if model_rule else [],
            )

            # NOTE: these calls used to be gated on `model_id not in self._registry`
            # so they only ran once per model_id for the process lifetime. That
            # meant an admin updating the AI Model Library's weights for an
            # already-loaded model_identifier was silently ignored until the
            # vision service restarted (same class of bug as the RTSP hot-reload
            # fix in stream_manager.py). Each _ensure_*_model() below is now
            # cheap to call every frame — it only re-resolves/reloads the model
            # when the rule's resolved artifact signature actually changed.
            if model_rule and rule_family == "restaurant-ppe":
                self._ensure_restaurant_model(model_id, model_rule)

            if model_rule and rule_family == "pest-detection":
                self._ensure_pest_model(model_id, model_rule)

            if model_rule and rule_family == "open-vocab":
                self._ensure_open_vocab_model(model_id, model_rule)

            if model_rule and rule_family == "pose-estimation":
                self._ensure_pose_model(model_id, model_rule)

            if model_rule and (model_id in ("human-detection-v1", "yolo26m") or "human" in model_id):
                self._ensure_human_detector(model_id, model_rule)

            # P1 Optimization #4: Interleaved Sub-Sampling for Distinct SOP Heavy Models
            should_run, cached_dets = self._should_run_cadence(
                camera_id, model_id, rule_family, model_rules
            )
            if not should_run:
                if cached_dets:
                    results.extend(cached_dets)
                continue

            model = self._registry.get(model_id)


            # ── Dynamic Gating & Cropping Strategy ──────────────────────────
            # Determine flags from the rules configured for this model:
            # 1. requires_human_presence: True if any active rule explicitly sets it.
            # 2. requires_cropping: True if any active rule explicitly sets it.
            has_db_model = any(bool(getattr(r, "ai_model_id", None)) for r in model_rules)
            if has_db_model:
                requires_human_presence = any(
                    bool(getattr(r, "model_requires_human_presence", False)) for r in model_rules
                )
                requires_cropping = any(
                    bool(getattr(r, "model_requires_cropping", False)) for r in model_rules
                )
            else:
                # Backward compatibility for unstubbed / legacy mock rules without DB ai_model_id
                requires_human_presence = any(
                    bool(getattr(r, "model_requires_human_presence", False)) for r in model_rules
                )
                if any(bool(getattr(r, "model_requires_cropping", False)) for r in model_rules):
                    requires_cropping = True
                elif rule_family == "restaurant-ppe":
                    requires_cropping = config.RESTAURANT_PPE_PERSON_CROP
                    requires_human_presence = True
                else:
                    requires_cropping = False

            # If the model requires human presence, gate strictly on detected persons:
            if requires_human_presence and rule_family != "pose-estimation":
                persons = _persons()
                if not persons:
                    logger.debug(
                        "[%s] No persons detected and model requires human presence — skipping.",
                        model_id,
                    )
                    continue

            if requires_cropping:
                crop_dets = self._run_ppe_on_person_crops(
                    model, pil_image, _persons(), model_id, camera_id
                )
                results.extend(crop_dets)
                self._update_cadence_cache(camera_id, model_id, crop_dets)
                continue

            if isinstance(model, (RestaurantPpeDetector, PestDetector)):
                dets = model.predict(pil_image, source_model=model_id)
                results.extend(dets)
                self._update_cadence_cache(camera_id, model_id, dets)
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
                min_conf = (
                    self._model_confidence(model_rule, "open-vocab")
                    if model_rule
                    else getattr(config, "MIN_CONFIDENCE_OPEN_VOCAB", 0.80)
                )
                ov_dets = model.predict(
                    pil_image,
                    source_model=model_id,
                    candidate_labels=candidate_labels,
                    min_confidence=min_conf,
                )
                results.extend(ov_dets)
                self._update_cadence_cache(camera_id, model_id, ov_dets)
                continue

            if rule_family == "pose-estimation" and model is not None:
                COCO_KEYPOINTS = [
                    "nose", "left_eye", "right_eye", "left_ear", "right_ear",
                    "left_shoulder", "right_shoulder", "left_elbow", "right_elbow",
                    "left_wrist", "right_wrist", "left_hip", "right_hip",
                    "left_knee", "right_knee", "left_ankle", "right_ankle"
                ]
                try:
                    with self._predict_lock(model):
                        conf = self._model_confidence(model_rule) if model_rule else 0.4
                        preds = model.predict(pil_image, conf=conf, verbose=False)

                    pose_dets: List[Dict] = []
                    for pred in preds:
                        if pred.boxes is None or len(pred.boxes) == 0:
                            continue
                        boxes = pred.boxes.xyxy.cpu().numpy()
                        confs = pred.boxes.conf.cpu().numpy()
                        kpts = pred.keypoints.xyn.cpu().numpy() if pred.keypoints is not None else None
                        kpts_conf = pred.keypoints.conf.cpu().numpy() if (pred.keypoints is not None and getattr(pred.keypoints, "conf", None) is not None) else None

                        for i in range(len(boxes)):
                            box = boxes[i]
                            score = float(confs[i])
                            kpt_dict = {}
                            if kpts is not None and i < len(kpts):
                                for idx, name in enumerate(COCO_KEYPOINTS):
                                    if idx < len(kpts[i]):
                                        kx, ky = float(kpts[i][idx][0]), float(kpts[i][idx][1])
                                        kc = float(kpts_conf[i][idx]) if kpts_conf is not None and idx < len(kpts_conf[i]) else 0.9
                                        kpt_dict[name] = (kx, ky, kc)

                            pose_dets.append({
                                "box": {
                                    "xmin": float(box[0]),
                                    "ymin": float(box[1]),
                                    "xmax": float(box[2]),
                                    "ymax": float(box[3]),
                                },
                                "score": score,
                                "label": "person",
                                "source_model": model_id,
                                "model_family": "ergonomics",
                                "keypoints": kpt_dict,
                                "person_box": {
                                    "xmin": float(box[0]),
                                    "ymin": float(box[1]),
                                    "xmax": float(box[2]),
                                    "ymax": float(box[3]),
                                }
                            })
                    results.extend(pose_dets)
                    self._update_cadence_cache(camera_id, model_id, pose_dets)
                except Exception as ergo_err:
                    logger.error("Pose estimation inference error on model '%s': %s", model_id, ergo_err)
                continue

            was_roboflow = False
            if model_id in self._roboflow_map:
                results.extend(
                    self._run_roboflow_inference(
                        pil_image,
                        self._roboflow_map[model_id],
                        min_confidence=self._model_confidence(model_rule),
                    )
                )
                was_roboflow = True
            elif model_id in ("construction-site-safety/1",):
                results.extend(
                    self._run_roboflow_inference(
                        pil_image,
                        model_id,
                        min_confidence=self._model_confidence(model_rule),
                    )
                )
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
            if model_id in ("human-detection-v1", "yolo26m") and person_cache["detected"]:
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
                    # H3 fix: was `conf=0.25` literal — now env-tunable.
                    # V6 fix: serialise predict() on this shared model — it is
                    # called concurrently from every camera capture thread.
                    with self._predict_lock(model):
                        det_results = model.predict(
                            pil_image,
                            conf=config.GENERIC_YOLO_MIN_CONFIDENCE,
                            device=self.device,
                            verbose=False,
                        )

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
                elif hasattr(model, "predict") and callable(getattr(model, "predict")):
                    dets = model.predict(pil_image, source_model=model_id)
                    for d in dets:
                        d.setdefault("source_model", model_id)
                    results.extend(dets)
                else:
                    with self._legacy_lock:
                        # H3 fix: was `threshold=0.25` literal — now env-tunable.
                        detections = model(pil_image, threshold=config.HF_LEGACY_DETECTION_THRESHOLD)
                        for detection in detections:
                            detection["source_model"] = model_id
                            results.append(detection)

            except Exception as e:
                logger.error("Inference failed for model %s: %s", model_id, e)

        # Always include detected persons in results if any were detected on the frame.
        # This allows the tracking layer and attendance system to track all persons in the scene,
        # while rule_evaluator only fires for rules specifically configured with trigger_labels.
        if person_cache["detected"] and person_cache["boxes"]:
            existing_person_boxes = {
                (int(d.get("box", {}).get("xmin", 0)), int(d.get("box", {}).get("ymin", 0)))
                for d in results
                if d.get("label") == "person"
            }
            for pbox in person_cache["boxes"]:
                key = (int(pbox.get("xmin", 0)), int(pbox.get("ymin", 0)))
                if key not in existing_person_boxes:
                    results.append(
                        {
                            "label": "person",
                            "score": float(pbox.get("score", 0.9)),
                            "box": {k: int(pbox[k]) for k in ("xmin", "ymin", "xmax", "ymax")},
                            "source_model": "human-detection-v1",
                        }
                    )

        return results

    # ──────────────────────────────────────────────────────────────────────
    # Motion gate (with Safety Heartbeat Floor)
    # ──────────────────────────────────────────────────────────────────────

    def _frame_thumbnail(self, pil_image: Image.Image) -> np.ndarray:
        """Downscale to a tiny grayscale thumbnail for cheap motion comparison."""
        size = max(32, int(getattr(config, "MOTION_GATE_SAMPLE_SIZE", 160)))
        thumb = pil_image.convert("L").resize((size, size), Image.BILINEAR)
        return np.asarray(thumb, dtype=np.int16)

    def _maybe_gated_persons(self, pil_image: Image.Image, camera_id: Optional[str]) -> Optional[List[Dict]]:
        """
        Return cached person_boxes when motion is below threshold AND within the
        safety heartbeat floor; returns None if motion was detected, the heartbeat
        floor expired, or the camera has no previous state (forcing a real YOLO pass).
        """
        if not (getattr(config, "MOTION_GATE_ENABLED", False) and camera_id):
            return None

        if not hasattr(self, "_motion_cache"):
            self._motion_cache = {}

        prev = self._motion_cache.get(camera_id)
        if not prev or prev.get("thumb") is None:
            return None

        # P1 Optimization #5: Safety Heartbeat Floor
        # Force fresh evaluation if time elapsed exceeds MOTION_GATE_HEARTBEAT_SECONDS (default 1.5s),
        # preventing false negatives if a stationary subject momentarily removes PPE without breaching motion threshold.
        now = time.monotonic()
        last_eval_time = prev.get("last_eval_time", 0.0)
        heartbeat_sec = float(getattr(config, "MOTION_GATE_HEARTBEAT_SECONDS", 1.5))
        if (now - last_eval_time) >= heartbeat_sec:
            return None

        try:
            curr = self._frame_thumbnail(pil_image)
            diff = float(np.mean(np.abs(curr - prev["thumb"])))
        except Exception:  # noqa: BLE001
            return None

        if diff >= getattr(config, "MOTION_GATE_THRESHOLD", 5.0):
            return None

        # Refresh thumb (slow drift would otherwise never re-trigger detection).
        prev["thumb"] = curr
        cached = prev.get("persons") or []
        # Return deep copy of dicts so downstream mutation can't corrupt the cache.
        copied_persons = []
        for p in cached:
            cp = dict(p)
            if isinstance(p.get("box"), dict):
                cp["box"] = dict(p["box"])
            copied_persons.append(cp)
        return copied_persons

    def _update_motion_cache(self, camera_id: Optional[str], pil_image: Image.Image, persons: List[Dict]) -> None:
        if not (getattr(config, "MOTION_GATE_ENABLED", False) and camera_id):
            return
        if not hasattr(self, "_motion_cache"):
            self._motion_cache = {}
        try:
            cached_persons = []
            for p in (persons or []):
                cp = dict(p)
                if isinstance(p.get("box"), dict):
                    cp["box"] = dict(p["box"])
                cached_persons.append(cp)

            self._motion_cache[camera_id] = {
                "thumb": self._frame_thumbnail(pil_image),
                "persons": cached_persons,
                "last_eval_time": time.monotonic(),
            }
        except Exception:  # noqa: BLE001
            pass

    def clear_motion_cache(self, camera_id: Optional[str] = None) -> None:
        """Clear motion gate state for a specific camera or all cameras."""
        if not hasattr(self, "_motion_cache"):
            return
        if camera_id is not None:
            self._motion_cache.pop(camera_id, None)
        else:
            self._motion_cache.clear()

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

        # P0 Optimization #3: Central Dynamic Batcher
        # Coalesces concurrent camera frames into a single forward pass without lock contention
        if (
            self._person_batcher is not None
            and getattr(config, "DYNAMIC_BATCHING_ENABLED", True)
        ):
            try:
                return self._person_batcher.submit(pil_image)
            except Exception as e:
                logger.error(
                    "Dynamic batch person detection failed, falling back to direct lock: %s",
                    e,
                )

        try:
            # Fallback: serialise predict() on the shared person detector
            with self._predict_lock(model):
                det_results = model.predict(
                    pil_image,
                    conf=config.PERSON_DETECTOR_CONFIDENCE,
                    classes=[0],  # COCO 'person'
                    device=self.device,
                    verbose=False,
                )
            persons: List[Dict] = []
            for result in det_results:
                boxes = getattr(result, "boxes", None)
                if boxes is not None:
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
        ppe_model: Any,
        pil_image: Image.Image,
        person_boxes: List[Dict],
        source_model: str,
        camera_id: str | None = None,
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
        (M4 fix: actual default is 0.20 in config.py, not the 0.25 the old
        docstring claimed) rather than re-enabling the full-frame fallback.

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

        # Collect all valid crops and their frame-offset metadata
        crops = []
        crop_metadata = []

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
            crops.append(crop)
            crop_metadata.append({"offset": (x1, y1), "pbox": pbox})

        if not crops:
            return []

        # P0 Optimization #2: Chunk crops into batches capped at MAX_CROP_BATCH_SIZE
        # (default 8) to prevent GPU OOM on edge accelerators (e.g. Jetson Orin Nano).
        max_batch_size = max(1, int(getattr(config, "MAX_CROP_BATCH_SIZE", 8)))
        all_dets: List[Dict] = []

        for i in range(0, len(crops), max_batch_size):
            sub_crops = crops[i : i + max_batch_size]
            sub_meta = crop_metadata[i : i + max_batch_size]

            try:
                if hasattr(ppe_model, "predict_batch"):
                    batch_dets = ppe_model.predict_batch(sub_crops, source_model=source_model)
                else:
                    batch_dets = [ppe_model.predict(c, source_model=source_model) for c in sub_crops]

                logger.info(
                    "[%s] Person crops batch (%d to %d of %d) -> PPE model '%s' processed",
                    camera_id, i + 1, min(i + max_batch_size, len(crops)), len(crops), source_model
                )
            except Exception as e:  # noqa: BLE001
                logger.error("PPE inference on person crop batch failed: %s", e)
                continue

            for crop_dets, meta in zip(batch_dets, sub_meta):
                ox, oy = meta["offset"]
                pbox = meta["pbox"]
                for d in crop_dets:
                    b = d["box"]
                    b["xmin"] += ox
                    b["ymin"] += oy
                    b["xmax"] += ox
                    b["ymax"] += oy
                    d["person_box"] = {
                        "xmin": pbox["xmin"],
                        "ymin": pbox["ymin"],
                        "xmax": pbox["xmax"],
                        "ymax": pbox["ymax"],
                    }
                    d["person_score"] = pbox.get("score")

                    # Anatomical bounding box clamp:
                    # Some custom YOLO models train on full upper bodies for hairnet/mask labels,
                    # causing the bounding box to engulf the entire torso. Clamp head PPE to the
                    # top ~38% of the person's bounding box so boxes stay neatly on the head/hair.
                    lbl = str(d.get("label") or "").lower()
                    if lbl in ("no-hairnet", "missing-hairnet", "hairnet", "hair-cover"):
                        person_h = pbox["ymax"] - pbox["ymin"]
                        if person_h > 0:
                            max_hairnet_bottom = pbox["ymin"] + int(person_h * 0.38)
                            if b["ymax"] > max_hairnet_bottom:
                                b["ymax"] = max_hairnet_bottom

                    all_dets.append(d)

        # H3 fix: was `iou_threshold=0.45` literal — now env-tunable.
        return self._nms_dets(all_dets, iou_threshold=config.NMS_IOU_THRESHOLD)

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

    def _run_roboflow_inference(self, pil_image: Image.Image, model_id: str, min_confidence: float) -> List[Dict]:
        if not self._roboflow_client:
            return []

        try:
            np_img = np.array(pil_image)
            result = self._roboflow_client.infer(np_img, model_id=model_id)
            predictions = result.get("predictions", [])

            std_results = []
            for prediction in predictions:
                score = prediction.get("confidence", 0.0)
                if score < min_confidence:
                    continue
                cx, cy = prediction["x"], prediction["y"]
                w, h = prediction["width"], prediction["height"]

                std_results.append(
                    {
                        "label": prediction.get("class", "unknown").lower(),
                        "score": score,
                        "box": {
                            "xmin": int(cx - (w / 2.0)),
                            "ymin": int(cy - (h / 2.0)),
                            "xmax": int(cx + (w / 2.0)),
                            "ymax": int(cy + (h / 2.0)),
                        },
                        "source_model": model_id,
                        "min_confidence": min_confidence,
                    }
                )
            return std_results
        except Exception as e:
            logger.error("Roboflow API failed: %s", e)
            return []

    def close(self):
        """Cleanly stop background dynamic batch schedulers and reset cadence and motion caches."""
        self.clear_cadence_cache()
        self.clear_motion_cache()
        if self._person_batcher is not None:
            self._person_batcher.stop()
            self._person_batcher = None
        for detector in list(self._restaurant_detectors_by_path.values()):
            if hasattr(detector, "close"):
                try:
                    detector.close()
                except Exception:
                    pass

    def __del__(self):
        try:
            self.close()
        except Exception:
            pass


if __name__ == "__main__":
    pass
