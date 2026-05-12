import os
import requests
import logging
from PIL import Image
import numpy as np

logger = logging.getLogger("vision-service.face_recognizer")

try:
    import face_recognition
    HAS_FACE_RECOGNITION = True
except ImportError:
    HAS_FACE_RECOGNITION = False
    logger.warning("face_recognition library not installed. Facial recognition is disabled.")

# Assuming Human ReID service is accessible via this URL in the Aspire environment
# AppHost injects Services__Reid__HttpUrl or similar? 
# Wait, let's use the env var or default.
REID_URL = os.getenv("Services__reid__http__0", "http://localhost:5004")

def identify_person(rgb_frame: np.ndarray, person_box: dict, tenant_id: str) -> dict:
    """
    Crops the person from the frame, extracts a face embedding, and queries the ReID service.
    Returns {"employeeId": str, "isUnauthorized": bool}
    """
    if not HAS_FACE_RECOGNITION:
        return {"employeeId": None, "isUnauthorized": False}

    try:
        xmin, ymin, xmax, ymax = person_box["xmin"], person_box["ymin"], person_box["xmax"], person_box["ymax"]
        
        # Add some padding to the box
        h, w = rgb_frame.shape[:2]
        padding = 20
        xmin = max(0, xmin - padding)
        ymin = max(0, ymin - padding)
        xmax = min(w, xmax + padding)
        ymax = min(h, ymax + padding)

        person_crop = rgb_frame[ymin:ymax, xmin:xmax]
        
        # face_recognition works on RGB numpy arrays
        face_locations = face_recognition.face_locations(person_crop, model="hog") # hog is faster than cnn
        
        if not face_locations:
            logger.debug("No face found in person crop.")
            return {"employeeId": None, "isUnauthorized": True} # Unknown person

        # Extract embeddings
        face_encodings = face_recognition.face_encodings(person_crop, face_locations)
        
        if not face_encodings:
            return {"employeeId": None, "isUnauthorized": True}

        # We take the largest face by default, or just the first one if hog usually returns the most prominent
        # Actually face_encodings returns in same order as face_locations
        largest_face_idx = 0
        max_area = 0
        for i, loc in enumerate(face_locations):
            top, right, bottom, left = loc
            area = (bottom - top) * (right - left)
            if area > max_area:
                max_area = area
                largest_face_idx = i

        primary_encoding = face_encodings[largest_face_idx]
        
        # Query ReID Service
        search_payload = {
            "tenant_id": tenant_id,
            "embedding": primary_encoding.tolist(),
            "top_k": 1,
            "threshold": 0.6
        }
        
        search_url = f"{REID_URL.rstrip('/')}/search"
        response = requests.post(search_url, json=search_payload, timeout=2.0)
        
        if response.status_code == 200:
            results = response.json()
            if results and len(results) > 0:
                best_match = results[0]
                return {"employeeId": best_match["person_id"], "isUnauthorized": False}
            else:
                return {"employeeId": None, "isUnauthorized": True}
        else:
            logger.error(f"ReID service search failed: {response.text}")
            return {"employeeId": None, "isUnauthorized": False} # Fail open

    except Exception as e:
        logger.error(f"Facial recognition error: {e}")
        return {"employeeId": None, "isUnauthorized": False}
