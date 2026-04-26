"""
data_collector.py
System for collecting AI performance data, low-confidence frames, and 
user feedback for future model training and fine-tuning.
"""
import os
import json
import uuid
import logging
from datetime import datetime
from PIL import Image
from typing import Dict, List, Any

logger = logging.getLogger("vision-service.data-collector")

class DataCollector:
    def __init__(self, base_path: str = "captured_data"):
        self.base_path = base_path
        self.raw_frames_path = os.path.join(base_path, "raw_frames")
        self.metadata_path = os.path.join(base_path, "metadata")
        
        # Ensure directories exist
        os.makedirs(self.raw_frames_path, exist_ok=True)
        os.makedirs(self.metadata_path, exist_ok=True)
        
        logger.info("📡 Data Collector initialized at %s", self.base_path)

    def collect_inference_event(self, pil_image: Image.Image, detections: List[Dict], camera_id: str, tenant_id: str):
        """
        Evaluates detections and saves frames that are 'interesting' 
        (e.g., low confidence or high-risk violations).
        """
        # Logic for 'interesting' events:
        # 1. Any detection with confidence between 0.2 and 0.6 (ambiguous)
        # 2. Any 'Safety' violation (high importance)
        # 3. Random sampling (1 in 100) for baseline accuracy tracking
        
        is_interesting = False
        low_confidence_threshold = 0.6
        min_confidence_threshold = 0.2
        
        for d in detections:
            score = d.get("score", 0)
            if min_confidence_threshold < score < low_confidence_threshold:
                is_interesting = True
                d["collection_reason"] = "low_confidence"
                break
            
            # You can add more logic here based on violation types if passed
            
        if is_interesting:
            self.save_event(pil_image, detections, camera_id, tenant_id)

    def save_event(self, pil_image: Image.Image, detections: List[Dict], camera_id: str, tenant_id: str, reason: str = "auto_collect"):
        """
        Saves the frame and its detection metadata to disk.
        """
        event_id = str(uuid.uuid4())
        timestamp = datetime.utcnow().isoformat()
        
        # 1. Save Image
        image_filename = f"{event_id}.jpg"
        image_path = os.path.join(self.raw_frames_path, image_filename)
        pil_image.save(image_path, "JPEG", quality=85)
        
        # 2. Save Metadata
        metadata = {
            "event_id": event_id,
            "timestamp": timestamp,
            "camera_id": camera_id,
            "tenant_id": tenant_id,
            "detections": detections,
            "collection_reason": reason,
            "image_path": image_filename
        }
        
        meta_filename = f"{event_id}.json"
        meta_path = os.path.join(self.metadata_path, meta_filename)
        
        with open(meta_path, 'w') as f:
            json.dump(metadata, f, indent=2)
            
        logger.info("📸 Saved interesting event %s (Reason: %s)", event_id, reason)
        return event_id

    def handle_user_feedback(self, event_id: str, is_correct: bool, corrected_label: str = None):
        """
        Updates metadata with user feedback (True Positive / False Positive).
        """
        meta_path = os.path.join(self.metadata_path, f"{event_id}.json")
        if not os.path.exists(meta_path):
            logger.error("❌ Cannot find metadata for event %s", event_id)
            return
            
        with open(meta_path, 'r') as f:
            data = json.load(f)
            
        data["user_feedback"] = {
            "is_correct": is_correct,
            "corrected_label": corrected_label,
            "feedback_timestamp": datetime.utcnow().isoformat()
        }
        
        with open(meta_path, 'w') as f:
            json.dump(data, f, indent=2)
            
        logger.info("🗳️ Feedback recorded for event %s: %s", event_id, "Correct" if is_correct else "Incorrect")
