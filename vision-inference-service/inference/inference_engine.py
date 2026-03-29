"""
inference/inference_engine.py
Modularized inference execution engine.
Handles AI model loading, locking, and execution.
"""
import logging
import threading
from typing import List, Dict, Set
from PIL import Image
from transformers import pipeline
from inference_sdk import InferenceHTTPClient
import numpy as np
import config

logger = logging.getLogger("vision-service.inference")

class InferenceEngine:
    def __init__(self):
        self._lock = threading.Lock()
        self._registry = {}
        self._load_models()

    def _load_models(self):
        logger.info("Loading object detection models into registry... (this may take a moment)")
        self._registry = {
            "human-detection-v1": pipeline("object-detection", model="hustvl/yolos-tiny"),
            "hygiene-monitor": pipeline("zero-shot-object-detection", model="google/owlvit-base-patch32"),
        }
        
        logger.info("Initializing Roboflow inference client...")
        try:
            self._roboflow_client = InferenceHTTPClient(
                api_url="https://detect.roboflow.com",
                api_key=config.ROBOFLOW_API_KEY
            )
        except Exception as e:
            logger.error("Failed to initialize Roboflow client. Check ROBOFLOW_API_KEY. %s", e)
            self._roboflow_client = None
            
        logger.info("✅ Models loaded")

    def run_inference(self, pil_image: Image.Image, active_rules: List) -> List[Dict]:
        """
        Runs inference on the provided image based on the active rules.
        Handles both standard object detection and zero-shot detection.
        Thread-safe structure prevents CPU/GPU thrashing.
        """
        results = []
        unique_models = {rule.model_identifier for rule in active_rules}
        
        # Lock to prevent multiple threads from hammering the models concurrently
        with self._lock:
            for model_id in unique_models:
                # 1. Handle HTTP Models (Roboflow)
                if model_id in ("kitchenhygiene/2", "construction-site-safety/1"):
                    if not self._roboflow_client:
                        logger.error("❌ Roboflow client not initialized. Skipping kitchenhygiene/2.")
                        continue
                        
                    logger.info("🧠 Sending image via HTTP to Roboflow %s...", model_id)
                    try:
                        # Roboflow SDK doesn't natively accept PIL directly without saving or numpy manipulation.
                        # Convert PIL to BGR numpy array which inference_sdk understands seamlessly via cv2 style array,
                        # or we can pass a JPEG byte string / file path. Wait, inference_sdk infer() accepts numpy array directly.
                        # The safest cross-platform way is to convert the PIL image to a standard numpy array or an image file on disk. 
                        # Or, even better, passing a numpy array. 
                        # Since we don't import cv2 in this file yet (but we can), let's use the PIL -> Numpy array since inference_sdk uses cv2 internally.
                        # A much safer bet is passing the image as a base64 or temporary array. 
                        # Wait, inference_sdk `infer()` can take a numpy array directly (cv2 image). Let's convert PIL to RGB-numpy.
                        np_img = np.array(pil_image)
                        
                        # Note: Roboflow typically expects BGR if using cv2 directly, but `infer()` handles RGB arrays fine in newer SDKs.
                        result = self._roboflow_client.infer(np_img, model_id=model_id)
                        
                        predictions = result.get("predictions", [])
                        logger.info("✅ %s returned %d raw detections", model_id, len(predictions))
                        
                        # Standardize Roboflow's {class, confidence, x, y, width, height} 
                        # (x, y are the CENTER of the bounding box)
                        # into {label, score, box: {xmin, ymin, xmax, ymax}}
                        for p in predictions:
                            cx = p["x"]
                            cy = p["y"]
                            w = p["width"]
                            h = p["height"]
                            
                            xmin = cx - (w / 2.0)
                            xmax = cx + (w / 2.0)
                            ymin = cy - (h / 2.0)
                            ymax = cy + (h / 2.0)
                            
                            std_det = {
                                "label": p.get("class", "unknown").lower(),
                                "score": p.get("confidence", 0.0),
                                "box": {
                                    "xmin": int(xmin),
                                    "ymin": int(ymin),
                                    "xmax": int(xmax),
                                    "ymax": int(ymax)
                                },
                                "source_model": model_id
                            }
                            logger.debug("  - Detected '%s' (Score: %.3f) at %s", std_det['label'], std_det['score'], std_det['box'])
                            results.append(std_det)
                    except Exception as e:
                        logger.error("❌ Roboflow API failed: %s", e)
                    continue

                # 2. Handle Local Models (HuggingFace)
                model = self._registry.get(model_id)
                if not model:
                    logger.error("❌ Model '%s' NOT FOUND in registry! Available: %s", 
                                 model_id, list(self._registry.keys()))
                    continue

                try:
                    if model_id == "hygiene-monitor":
                        # Zero-shot detection needs candidate labels extracted from ALL rules
                        candidate_labels = []
                        for rule in active_rules:
                            if rule.model_identifier == model_id:
                                candidate_labels.extend(rule.trigger_labels)
                        
                        candidate_labels = list(set(candidate_labels))
                        if not candidate_labels:
                            logger.debug("No candidate labels found for hygiene-monitor. Skipping.")
                            continue
                            
                        logger.info("🧠 Sending image to %s with candidate labels: %s", model_id, candidate_labels)
                        detections = model(pil_image, candidate_labels=candidate_labels, threshold=0.05)
                    else:
                        logger.info("🧠 Sending image to %s...", model_id)
                        detections = model(pil_image, threshold=0.2)
                        
                    logger.info("✅ %s returned %d raw detections", model_id, len(detections))
                        
                    for d in detections:
                        d["source_model"] = model_id
                        logger.debug("  - Detected '%s' (Score: %.3f) at %s", d['label'], d['score'], d['box'])
                    results.extend(detections)
                    
                except Exception as e:
                    logger.error("Model inference failed for model %s: %s", model_id, e)

        return results
