"""
inference/inference_engine.py
Modularized inference execution engine.
Optimized for Apple Silicon (MPS) and high-performance YOLO models.
"""
import logging
import threading
import torch
from typing import List, Dict, Set, Any
from PIL import Image
import numpy as np
import config

try:
    from ultralytics import YOLO, YOLOWorld
    HAS_ULTRALYTICS = True
except ImportError:
    HAS_ULTRALYTICS = False
    
from transformers import pipeline
from inference_sdk import InferenceHTTPClient

logger = logging.getLogger("vision-service.inference")

class InferenceEngine:
    def __init__(self):
        # We keep a lock for legacy transformers pipelines which might not be thread-safe for concurrent forward passes on some setups
        self._legacy_lock = threading.Lock()
        self._registry = {}
        
        # Determine best device
        if torch.backends.mps.is_available():
            self.device = "mps"
            logger.info("🚀 Apple Silicon MPS (Metal) acceleration available.")
        elif torch.cuda.is_available():
            self.device = "cuda"
            logger.info("🚀 NVIDIA CUDA acceleration available.")
        else:
            self.device = "cpu"
            logger.info("🐢 Using CPU for inference.")
            
        self._load_models()

    def _load_models(self):
        logger.info("Loading object detection models into registry...")
        
        if HAS_ULTRALYTICS:
            try:
                # Ensure model directory exists
                import os
                os.makedirs("/tmp/models", exist_ok=True)

                # 1. Standard Object Detection (YOLOv11 Nano - best speed/accuracy for Mac)
                # 'yolo11n.pt' will be downloaded automatically on first run
                logger.info("📦 Loading YOLOv11n...")
                self._registry["human-detection-v1"] = YOLO("/tmp/models/yolo11n.pt")
                
                # 2. Zero-Shot Object Detection (YOLO-World Small - replacing OWL-ViT)
                logger.info("📦 Loading fallback YOLO-World for Kitchen Hygiene...")
                self._registry["hygiene-monitor"] = YOLOWorld("/tmp/models/yolov8s-worldv2.pt")
                
            except Exception as e:
                logger.error("❌ Failed to load Ultralytics models: %s. Falling back to legacy transformers.", e)
        
        # Fallbacks / Legacy Registry
        try:
            if "human-detection-v1" not in self._registry:
                logger.warning("⚠️ Using legacy YOLOS-tiny (Transformers)")
                self._registry["human-detection-v1-legacy"] = pipeline(
                    "object-detection", 
                    model="hustvl/yolos-tiny", 
                    device=self.device if self.device != "mps" else -1 # Transformers MPS support varies
                )
                
            if "hygiene-monitor" not in self._registry:
                 logger.warning("⚠️ Using legacy OWL-ViT (Transformers)")
                 self._registry["hygiene-monitor-legacy"] = pipeline(
                     "zero-shot-object-detection", 
                     model="google/owlvit-base-patch32",
                     device=self.device if self.device != "mps" else -1
                 )
        except Exception as e:
            logger.error("❌ Critical failure loading legacy models: %s", e)

        # Roboflow Client
        logger.info("Initializing Roboflow inference client...")
        try:
            self._roboflow_client = InferenceHTTPClient(
                api_url="https://detect.roboflow.com",
                api_key=config.ROBOFLOW_API_KEY
            )
        except Exception as e:
            logger.error("Failed to initialize Roboflow client. Check ROBOFLOW_API_KEY. %s", e)
            self._roboflow_client = None
            
        # Roboflow Logical to Physical Model Map
        self._roboflow_map = {
            "restaurant-hygiene-v1": "kitchenhygiene/2",
            "construction-site-safety-v1": "construction-site-safety/1"
        }
            
        logger.info("✅ Models loaded and ready on %s", self.device)

    def run_inference(self, pil_image: Image.Image, active_rules: List) -> List[Dict]:
        """
        Runs inference on the provided image based on the active rules.
        Optimized for high-throughput and Apple Silicon.
        """
        results = []
        unique_model_ids = {rule.model_identifier for rule in active_rules}
        
        for model_id in unique_model_ids:
            # 1. Handle Roboflow (External API) via Map or Legacy check
            was_roboflow = False
            if model_id in self._roboflow_map:
                roboflow_id = self._roboflow_map[model_id]
                results.extend(self._run_roboflow_inference(pil_image, roboflow_id))
                was_roboflow = True
            elif model_id in ("kitchenhygiene/2", "construction-site-safety/1"):
                results.extend(self._run_roboflow_inference(pil_image, model_id))
                was_roboflow = True
            
            # 2. SPECIAL: If this is a hygiene model, ALWAYS run local YOLO-World 
            # to detect the gear (hairnets, gloves, masks) which our spatial logic needs.
            mid_clean = str(model_id).lower().strip()
            if "hygiene" in mid_clean or mid_clean == "restaurant-hygiene-v1":
                logger.info("⚡ Running Local Hygiene Fallback for %s...", model_id)
                local_hygiene_results = self._run_local_hygiene_fallback(pil_image)
                logger.info("⚡ Local Fallback found %d objects.", len(local_hygiene_results))
                results.extend(local_hygiene_results)
            
            if was_roboflow:
                continue

            # 3. Handle Local Models (non-hygiene)
            model = self._registry.get(model_id)
            if not model:
                # Try legacy fallback
                model = self._registry.get(f"{model_id}-legacy")
                if not model:
                    logger.error("❌ Model '%s' NOT FOUND in registry!", model_id)
                    continue

            try:
                # Case A: Ultralytics (YOLO / YOLO-World)
                if HAS_ULTRALYTICS and isinstance(model, (YOLO, YOLOWorld)):
                    if model_id == "hygiene-monitor":
                        # YOLO-World needs dynamic labels set
                        candidate_labels = []
                        for rule in active_rules:
                            if rule.model_identifier == model_id:
                                candidate_labels.extend(rule.trigger_labels)
                        
                        candidate_labels = list(set(candidate_labels))
                        if candidate_labels:
                            model.set_classes(candidate_labels)
                        
                    # Run inference with MPS/GPU acceleration
                    # stream=True for memory efficiency, persist=True for tracking (optional)
                    det_results = model.predict(pil_image, conf=0.25, device=self.device, verbose=False)
                    
                    for r in det_results:
                        boxes = r.boxes
                        for i in range(len(boxes)):
                            box = boxes[i].xyxy[0].cpu().numpy() # [xmin, ymin, xmax, ymax]
                            label_idx = int(boxes[i].cls[0])
                            label = r.names[label_idx]
                            score = float(boxes[i].conf[0])
                            
                            results.append({
                                "label": label.lower(),
                                "score": score,
                                "box": {
                                    "xmin": int(box[0]),
                                    "ymin": int(box[1]),
                                    "xmax": int(box[2]),
                                    "ymax": int(box[3])
                                },
                                "source_model": model_id
                            })

                # Case B: Legacy Transformers
                else:
                    with self._legacy_lock:
                        if model_id == "hygiene-monitor":
                            candidate_labels = []
                            for rule in active_rules:
                                if rule.model_identifier == model_id:
                                    candidate_labels.extend(rule.trigger_labels)
                            
                            candidate_labels = list(set(candidate_labels))
                            if not candidate_labels: continue
                            
                            detections = model(pil_image, candidate_labels=candidate_labels, threshold=0.1)
                        else:
                            detections = model(pil_image, threshold=0.25)
                            
                        for d in detections:
                            d["source_model"] = model_id
                            results.append(d)

            except Exception as e:
                logger.error("❌ Inference failed for model %s: %s", model_id, e)

        return results

    def _run_roboflow_inference(self, pil_image: Image.Image, model_id: str) -> List[Dict]:
        if not self._roboflow_client:
            return []
            
        try:
            np_img = np.array(pil_image)
            result = self._roboflow_client.infer(np_img, model_id=model_id)
            predictions = result.get("predictions", [])
            
            std_results = []
            for p in predictions:
                cx, cy = p["x"], p["y"]
                w, h = p["width"], p["height"]
                
                std_results.append({
                    "label": p.get("class", "unknown").lower(),
                    "score": p.get("confidence", 0.0),
                    "box": {
                        "xmin": int(cx - (w / 2.0)),
                        "ymin": int(cy - (h / 2.0)),
                        "xmax": int(cx + (w / 2.0)),
                        "ymax": int(cy + (h / 2.0))
                    },
                    "source_model": model_id
                })
            return std_results
        except Exception as e:
            logger.error("❌ Roboflow API failed: %s", e)
            return []

    def _run_local_hygiene_fallback(self, pil_image: Image.Image) -> List[Dict]:
        """
        Runs the local YOLO-World model with specialized hygiene prompts.
        Used to provide robust detection of hairnets, gloves, and masks.
        """
        model = self._registry.get("hygiene-monitor")
        if not model or not isinstance(model, YOLOWorld):
            return []
            
        try:
            # Pitch perfect labels for Kitchen SOPs (Added person for baseline)
            classes = ["person", "hairnet", "gloves", "surgical mask", "apron", "hand", "helmet"]
            model.set_classes(classes)
            
            det_results = model.predict(pil_image, conf=0.25, device=self.device, verbose=False)
            local_results = []
            
            for r in det_results:
                boxes = r.boxes
                logger.debug("⚡ Local Fallback raw box count: %d", len(boxes))
                for i in range(len(boxes)):
                    box = boxes[i].xyxy[0].cpu().numpy()
                    label_idx = int(boxes[i].cls[0])
                    label = r.names[label_idx]
                    score = float(boxes[i].conf[0])
                    logger.debug("   - Raw Match: %s (%.2f)", label, score)
                    
                    # Map 'surgical mask' to 'mask' for evaluator compatibility
                    if label == "surgical mask": label = "mask"
                    
                    local_results.append({
                        "label": label.lower(),
                        "score": score,
                        "box": {
                            "xmin": int(box[0]),
                            "ymin": int(box[1]),
                            "xmax": int(box[2]),
                            "ymax": int(box[3])
                        },
                        "source_model": "hygiene-monitor" # Identified for evaluator
                    })
            return local_results
        except Exception as e:
            logger.error("❌ Local hygiene fallback failed: %s", e)
            return []

if __name__ == "__main__":
    pass
