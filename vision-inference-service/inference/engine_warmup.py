"""
inference/engine_warmup.py
==========================
Warm-up and precision parity validation for inference engines (TensorRT, ONNX, and PyTorch).

Problem 8 Solution:
1. Cold-Start Stalls:
   First forward pass on TensorRT, ONNX, or PyTorch CUDA/MPS triggers kernel compilation,
   CUDA tactic benchmarking, and memory pinning. If executed during live stream ingestion,
   this produces a 3-10s freeze causing frame drops and RTSP watchdog disconnections.
   Executing dummy passes at lifespan startup primes all kernel caches before stream connections.

2. Precision Divergence:
   FP16 quantization on TensorRT or CUDA can cause confidence drift. Parity validation
   verifies that detection confidences remain within acceptable bounds (default +/- 5%)
   relative to the FP32 reference model.
"""
import logging
import time
from typing import Any, Dict, List, Optional, Tuple, Union

import numpy as np

import config

logger = logging.getLogger("vision-service.inference.engine-warmup")


def warmup_model(
    model: Any,
    model_id: str,
    passes: int = 3,
    image_size: int = 640,
) -> bool:
    """
    Execute dummy inference passes through a model or detector wrapper to prime
    CUDA/MPS kernels, TensorRT execution plans, and dynamic batchers.
    """
    if model is None:
        return False

    dummy_frame = np.zeros((image_size, image_size, 3), dtype=np.uint8)
    t0 = time.monotonic()
    success_passes = 0

    for pass_idx in range(passes):
        try:
            # 1. Custom detector wrappers (RestaurantPpeDetector, UnifiedMultiTaskBackbone, etc.)
            if hasattr(model, "predict"):
                # Inspect signature or call directly
                try:
                    model.predict(dummy_frame)
                except TypeError:
                    # Some predict signatures require source_model or labels
                    try:
                        model.predict(dummy_frame, source_model=model_id)
                    except TypeError:
                        model.predict(dummy_frame, candidate_labels=["person", "item"])
            elif hasattr(model, "predict_flat"):
                model.predict_flat(dummy_frame)
            # 2. Raw Ultralytics YOLO or similar callable
            elif callable(model):
                model(dummy_frame, verbose=False)
            success_passes += 1
        except Exception as e:
            logger.warning(
                "Warm-up pass %d/%d for '%s' raised exception: %s",
                pass_idx + 1,
                passes,
                model_id,
                e,
            )
            break

    elapsed_ms = (time.monotonic() - t0) * 1000.0
    if success_passes > 0:
        logger.info(
            "🔥 Engine '%s' warmed up (%d/%d passes, %.2f ms total)",
            model_id,
            success_passes,
            passes,
            elapsed_ms,
        )
        return True
    logger.warning("Engine '%s' could not be warmed up (0 passes succeeded)", model_id)
    return False


def warmup_inference_engine(
    engine: Any,
    passes: Optional[int] = None,
) -> Dict[str, bool]:
    """
    Warm up all models currently in the inference engine registry and attached batchers.
    """
    if passes is None:
        passes = getattr(config, "MODEL_WARMUP_PASSES", 3)

    results: Dict[str, bool] = {}
    registry = getattr(engine, "_registry", {})

    logger.info("🔥 Starting inference engine warmup (passes=%d, models=%d)...", passes, len(registry))

    # 1. Warm up registry models
    for model_id, model in list(registry.items()):
        if model is None:
            continue
        ok = warmup_model(model, model_id=str(model_id), passes=passes)
        results[str(model_id)] = ok

    # 2. Warm up specialized detectors if present and not in registry
    detectors = getattr(engine, "_restaurant_detectors_by_path", {})
    for (path, conf), detector in list(detectors.items()):
        det_id = getattr(detector, "model_id", f"restaurant-{conf}")
        if det_id not in results:
            ok = warmup_model(detector, model_id=det_id, passes=passes)
            results[det_id] = ok

    # 3. Warm up dynamic person batcher if initialized
    person_batcher = getattr(engine, "_person_batcher", None)
    if person_batcher is not None and getattr(person_batcher, "is_running", False):
        try:
            dummy_img = np.zeros((640, 640, 3), dtype=np.uint8)
            # Submit dummy pass through batcher
            person_batcher.submit(dummy_img)
            time.sleep(0.02)
            logger.info("🔥 Person detector dynamic batcher warmed up")
        except Exception as be:
            logger.debug("Warmup submit for person batcher: %s", be)

    # 4. Warm up unified multi-task backbone if initialized
    unified_backbone = getattr(engine, "_unified_backbone", None)
    if unified_backbone is not None:
        ok = warmup_model(unified_backbone, model_id="unified-multitask-v1", passes=passes)
        results["unified-multitask-v1"] = ok

    logger.info("✅ Inference engine warmup completed: %d/%d models primed", sum(1 for v in results.values() if v), len(results))
    return results


def verify_fp16_fp32_parity(
    fp32_model: Any,
    fp16_model: Any,
    sample_input: np.ndarray,
    max_delta: Optional[float] = None,
) -> Tuple[bool, float]:
    """
    Verify that an FP16 quantized/TensorRT model does not diverge in score
    by more than max_delta relative to its FP32 reference model.

    Returns:
        (is_parity_acceptable: bool, max_observed_delta: float)
    """
    if max_delta is None:
        max_delta = getattr(config, "FP16_PARITY_MAX_DELTA", 0.05)

    def _extract_scores(preds: Any) -> Dict[str, float]:
        scores: Dict[str, float] = {}
        if isinstance(preds, list):
            for d in preds:
                if isinstance(d, dict) and "label" in d and "score" in d:
                    lbl = str(d["label"]).lower()
                    sc = float(d["score"])
                    scores[lbl] = max(scores.get(lbl, 0.0), sc)
        elif hasattr(preds, "boxes") and preds.boxes is not None:
            boxes = preds.boxes
            names = getattr(preds, "names", {})
            for idx in range(len(boxes)):
                cls_id = int(boxes[idx].cls[0])
                name = names.get(cls_id, str(cls_id)).lower()
                sc = float(boxes[idx].conf[0])
                scores[name] = max(scores.get(name, 0.0), sc)
        return scores

    try:
        preds_32 = fp32_model.predict(sample_input) if hasattr(fp32_model, "predict") else fp32_model(sample_input)
        preds_16 = fp16_model.predict(sample_input) if hasattr(fp16_model, "predict") else fp16_model(sample_input)

        scores_32 = _extract_scores(preds_32)
        scores_16 = _extract_scores(preds_16)

        max_observed_delta = 0.0
        all_labels = set(scores_32.keys()) | set(scores_16.keys())
        for lbl in all_labels:
            s32 = scores_32.get(lbl, 0.0)
            s16 = scores_16.get(lbl, 0.0)
            delta = abs(s32 - s16)
            if delta > max_observed_delta:
                max_observed_delta = delta

        is_acceptable = max_observed_delta <= max_delta
        if not is_acceptable:
            logger.warning(
                "FP16/FP32 precision parity divergence: max delta %.4f exceeds threshold %.4f",
                max_observed_delta,
                max_delta,
            )
        else:
            logger.info("FP16/FP32 precision parity confirmed (max delta: %.4f <= %.4f)", max_observed_delta, max_delta)

        return is_acceptable, max_observed_delta
    except Exception as e:
        logger.error("Failed to run FP16/FP32 parity verification: %s", e)
        return False, 1.0
