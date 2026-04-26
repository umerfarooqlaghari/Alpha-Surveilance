"""
rtsp/violation_manager.py
Manages temporal deduplication, hysteresis, and state management for detections.
Includes a simple IoU-based tracker since the AI model is stateless.
"""
import time
import logging
from typing import List, Dict, Optional, Tuple
from datetime import datetime

logger = logging.getLogger(__name__)

class SimpleIouTracker:
    """
    Very basic tracker that persists IDs based on box overlap between frames.
    """
    def __init__(self, iou_threshold: float = 0.3, max_missing_frames: int = 30):
        self._iou_threshold = iou_threshold
        self._max_missing_frames = max_missing_frames
        self._next_id = 1
        # track_id -> { "box": [xmin, ymin, xmax, ymax], "missing": int, "label": str }
        self.tracks = {}

    def _calculate_iou(self, boxA, boxB) -> float:
        xA = max(boxA[0], boxB[0])
        yA = max(boxA[1], boxB[1])
        xB = min(boxA[2], boxB[2])
        yB = min(boxA[3], boxB[3])
        interArea = max(0, xB - xA) * max(0, yB - yA)
        # union = areaA + areaB - intersection
        areaA = (boxA[2] - boxA[0]) * (boxA[3] - boxA[1])
        areaB = (boxB[2] - boxB[0]) * (boxB[3] - boxB[1])
        iou = interArea / float(areaA + areaB - interArea + 1e-6)
        return iou

    def update(self, detections: List[Dict]) -> List[Dict]:
        """
        Input: list of detections {"label": str, "box": {"xmin", "ymin", "xmax", "ymax"}, "score": float}
        Output: detections with "track_id" injected.
        """
        new_tracks = {}
        matched_track_ids = set()
        
        # Format detections for easier matching
        incoming_detections = []
        for d in detections:
            box = [d["box"]["xmin"], d["box"]["ymin"], d["box"]["xmax"], d["box"]["ymax"]]
            incoming_detections.append({
                "label": d["label"],
                "box": box,
                "score": d["score"],
                "original": d
            })

        # Simple greedy matching
        for det in incoming_detections:
            best_iou = 0.0
            best_id = None
            
            for tid, track in self.tracks.items():
                if tid in matched_track_ids:
                    continue
                if det["label"] != track["label"]:
                    continue
                
                iou = self._calculate_iou(det["box"], track["box"])
                if iou > best_iou:
                    best_iou = iou
                    best_id = tid
            
            if best_id is not None and best_iou > self._iou_threshold:
                # Update existing track
                matched_track_ids.add(best_id)
                new_tracks[best_id] = {
                    "box": det["box"],
                    "missing": 0,
                    "label": det["label"]
                }
                det["original"]["track_id"] = best_id
            else:
                # New track
                tid = self._next_id
                self._next_id += 1
                new_tracks[tid] = {
                    "box": det["box"],
                    "missing": 0,
                    "label": det["label"]
                }
                det["original"]["track_id"] = tid

        # Handle missing tracks (temporal smoothing)
        for tid, track in self.tracks.items():
            if tid not in matched_track_ids:
                track["missing"] += 1
                if track["missing"] < self._max_missing_frames:
                    new_tracks[tid] = track
        
        self.tracks = new_tracks
        return detections


class ViolationManager:
    """
    State machine for managing violation transitions.
    States: Pending -> Active -> Cooldown
    """
    STATE_PENDING = "Pending"
    STATE_ACTIVE = "Active"
    STATE_COOLDOWN = "Cooldown"

    def __init__(self, entry_hysteresis: int = 5, exit_buffer: int = 10):
        self.entry_hysteresis = entry_hysteresis
        self.exit_buffer = exit_buffer
        
        # camera_id -> tracker
        self._trackers: Dict[str, SimpleIouTracker] = {}
        
        # camera_id -> track_id -> state info
        # Info: { state, frames_seen, frames_missing, last_trigger_at, violation_type }
        self._states: Dict[str, Dict[int, Dict]] = {}
        
        # Default cooldowns by type (can be overridden by DB later)
        self._cooldown_thresholds = {
            "Security": 60,   # 1 minute
            "Safety": 30,     # 30 seconds
            "Unauthorized Entry": 300, # 5 minutes
            "Loitering": 60
        }
        

    def _get_tracker(self, camera_id: str) -> SimpleIouTracker:
        if camera_id not in self._trackers:
            self._trackers[camera_id] = SimpleIouTracker()
        return self._trackers[camera_id]

    def _get_camera_states(self, camera_id: str) -> Dict[int, Dict]:
        if camera_id not in self._states:
            self._states[camera_id] = {}
        return self._states[camera_id]

    async def process_frame(self, camera_id: str, detections: List[Dict], violation_rules: List['ViolationRule']) -> List[Dict]:
        """
        Updates states and returns a list of violation payloads that should be sent to the API.
        """
        tracker = self._get_tracker(camera_id)
        camera_states = self._get_camera_states(camera_id)
        
        # 1. Update tracker (inject track_id)
        tracked_detections = tracker.update(detections)
        
        # 2. Track which (track_id, rule_id) pairs we saw this frame
        seen_this_frame = set()
        
        results_to_post = []
        now = time.time()

        # 3. Process each detection
        for det in tracked_detections:
            tid = det["track_id"]
            model_id = det.get("source_model")
            label = det["label"].lower()

            # Find matching rules for this specific detection
            matching_rules = [
                r for r in violation_rules 
                # If trigger_labels is empty, treat it as a wildcard (match all labels from this model)
                if r.model_identifier == model_id and (not r.trigger_labels or label in r.trigger_labels)
            ]

            if not matching_rules:
                # This detection doesn't match any configured rules for this camera
                continue

            for rule in matching_rules:
                sop_id = rule.sop_violation_type_id
                state_key = (tid, sop_id)
                seen_this_frame.add(state_key)

                if state_key not in camera_states:
                    camera_states[state_key] = {
                        "state": self.STATE_PENDING,
                        "frames_seen": 1,
                        "frames_missing": 0,
                        "last_trigger_at": 0,
                        "type": det["label"], # For legacy cooldown lookup if needed
                        "sop_id": sop_id
                    }
                else:
                    s = camera_states[state_key]
                    s["frames_missing"] = 0
                    
                    if s["state"] == self.STATE_PENDING:
                        s["frames_seen"] += 1
                        if s["frames_seen"] >= self.entry_hysteresis:
                            # TRANSITION: Pending -> Active
                            s["state"] = self.STATE_ACTIVE
                            s["last_trigger_at"] = now
                            
                            logger.warning(
                                "\n" + "=" * 50 + "\n"
                                "  🚨 VIOLATION DETECTED 🚨\n"
                                "  Camera : %s\n"
                                "  Label  : %s (score=%.2f)\n"
                                "  SOP ID : %s\n"
                                "  Track  : %s\n" +
                                "=" * 50,
                                camera_id, det['label'], det['score'], sop_id, tid
                            )
                            results_to_post.append(self._create_payload(det, camera_id, "New", sop_id, rule.model_identifier))
                    
                    elif s["state"] == self.STATE_ACTIVE:
                        results_to_post.append(self._create_payload(det, camera_id, "Update", sop_id, rule.model_identifier))
                    
                    elif s["state"] == self.STATE_COOLDOWN:
                        v_type = s["type"]
                        threshold = self._cooldown_thresholds.get(v_type, 60)
                        if now - s["last_trigger_at"] > threshold:
                            s["state"] = self.STATE_PENDING
                            s["frames_seen"] = 1

        # 4. Process IDs NOT seen this frame (Exit Buffer)
        to_delete = []
        for state_key, s in camera_states.items():
            if state_key not in seen_this_frame:
                s["frames_missing"] += 1
                
                if s["state"] == self.STATE_ACTIVE:
                    if s["frames_missing"] >= self.exit_buffer:
                        s["state"] = self.STATE_COOLDOWN
                        logger.info(f"[{camera_id}] Track {state_key[0]} (Type: {state_key[1]}) left frame -> Cooldown")
                
                elif s["state"] == self.STATE_PENDING:
                    if s["frames_missing"] >= self.exit_buffer:
                        to_delete.append(state_key)
                
                elif s["state"] == self.STATE_COOLDOWN:
                    if s["frames_missing"] > 1000:
                        to_delete.append(state_key)

        for key in to_delete:
            del camera_states[key]

        return results_to_post

    def _create_payload(self, det: Dict, camera_id: str, status: str, sop_id: str, model_identifier: str) -> Dict:
        """Helper to create the expected payload for the Violation API."""
        return {
            "TrackId": det["track_id"],
            "Label": det["label"],
            "Score": det["score"],
            "Box": det["box"],
            "StateStatus": status, # "New" or "Update"
            "SopViolationTypeId": sop_id,
            "ModelIdentifier": model_identifier,
            "Metadata": det
        }

    def update_settings(self, settings: Dict[str, int]):
        """Update cooldown thresholds dynamically."""
        self._cooldown_thresholds.update(settings)
        logger.info("ViolationManager settings updated: %s", self._cooldown_thresholds)
