"""
rtsp/violation_manager.py
Manages temporal deduplication, hysteresis, and state management for detections.
Includes a simple IoU-based tracker since the AI model is stateless.
"""
import time
import copy
import logging
import threading
import numpy as np
from scipy.optimize import linear_sum_assignment
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

        C-4 fix: uses the Hungarian algorithm (linear_sum_assignment) instead of
        greedy matching.  Greedy matching swaps IDs when two people cross paths
        (their boxes overlap maximally at the crossover point), corrupting dwell
        timers.  Optimal assignment minimises total 1−IoU cost so no crossing
        can produce a better global score by swapping.
        """
        new_tracks: Dict[int, dict] = {}
        matched_track_ids: set = set()
        matched_det_indices: set = set()

        # Normalise detections to a flat list with the original dict reference.
        incoming: List[Dict] = []
        for d in detections:
            box = [d["box"]["xmin"], d["box"]["ymin"], d["box"]["xmax"], d["box"]["ymax"]]
            incoming.append({"label": d["label"], "box": box, "score": d["score"], "original": d})

        # Per-label optimal assignment.
        for label in set(d["label"] for d in incoming):
            det_indices = [i for i, d in enumerate(incoming) if d["label"] == label]
            track_ids   = [tid for tid, t in self.tracks.items() if t["label"] == label]

            if not det_indices or not track_ids:
                continue

            nd, nt = len(det_indices), len(track_ids)
            cost = np.ones((nd, nt), dtype=float)  # default: 1.0 → no overlap
            for i, di in enumerate(det_indices):
                for j, tid in enumerate(track_ids):
                    iou = self._calculate_iou(incoming[di]["box"], self.tracks[tid]["box"])
                    if iou > 0:
                        cost[i, j] = 1.0 - iou

            row_ids, col_ids = linear_sum_assignment(cost)
            for r, c in zip(row_ids, col_ids):
                if cost[r, c] > (1.0 - self._iou_threshold):
                    continue  # IoU below threshold — treat as new track
                di  = det_indices[r]
                tid = track_ids[c]
                matched_det_indices.add(di)
                matched_track_ids.add(tid)
                new_tracks[tid] = {"box": incoming[di]["box"], "missing": 0, "label": label}
                incoming[di]["original"]["track_id"] = tid

        # Unmatched detections → new tracks.
        for i, det in enumerate(incoming):
            if i not in matched_det_indices:
                tid = self._next_id
                self._next_id += 1
                new_tracks[tid] = {"box": det["box"], "missing": 0, "label": det["label"]}
                det["original"]["track_id"] = tid

        # Unmatched existing tracks → increment missing counter.
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

    def __init__(self, entry_hysteresis: int = 5, exit_buffer: int = 10, max_states_per_camera: int = 1000):
        self.entry_hysteresis = entry_hysteresis
        self.exit_buffer = exit_buffer
        # Audit P3 #10: hard cap on state-dict size per camera so a
        # misbehaving tracker (e.g. flickering boxes that mint new track ids
        # every frame) can't OOM the service over days. Eviction uses the
        # last_trigger_at timestamp — oldest unfired Pending states go first.
        self.max_states_per_camera = max(64, int(max_states_per_camera))
        
        # camera_id -> tracker
        self._trackers: Dict[str, SimpleIouTracker] = {}
        
        # camera_id -> state dict
        # Info: { state, frames_seen, frames_missing, last_trigger_at, violation_type }
        self._states: Dict[str, Dict[int, Dict]] = {}

        # C-2 + I-2 fix: per-camera threading.Lock replaces the single global
        # asyncio.Lock.  Per-camera locks mean cameras don't block each other
        # (I-2).  threading.Lock (not asyncio.Lock) correctly serialises the
        # capture thread (tag_tracks) against the event-loop thread
        # (process_frame) for the SAME camera (C-2).  setdefault() is used for
        # thread-safe initialisation without a secondary mutex.
        self._camera_locks: Dict[str, threading.Lock] = {}
        
        # Default cooldowns by type (can be overridden by DB later).
        # Audit P3 #12: the original implementation indexed by legacy strings
        # like "Security"/"Safety" that no detection label ever produces,
        # so the cooldown lookup always returned the default. Kept here as
        # an empty dict so `update_settings(...)` from the DB still works,
        # but the cooldown phase now applies one uniform threshold.
        self._cooldown_thresholds: Dict[str, int] = {}
        self._default_cooldown_seconds = 60
        

    def _get_tracker(self, camera_id: str) -> SimpleIouTracker:
        if camera_id not in self._trackers:
            self._trackers[camera_id] = SimpleIouTracker()
        return self._trackers[camera_id]

    def _get_camera_states(self, camera_id: str) -> Dict[int, Dict]:
        if camera_id not in self._states:
            self._states[camera_id] = {}
        return self._states[camera_id]

    def _get_camera_lock(self, camera_id: str) -> threading.Lock:
        """Return (creating if necessary) the per-camera threading.Lock.

        ``dict.setdefault`` is atomic under the GIL so two threads that
        simultaneously encounter a new camera both get back the same Lock
        object without a secondary mutex.
        """
        return self._camera_locks.setdefault(camera_id, threading.Lock())

    def tag_tracks(self, camera_id: str, detections: List[Dict]) -> List[Dict]:
        """
        Inject ``track_id`` on each detection using this camera's IoU tracker.

        Called by the inference loop BEFORE rule evaluation so dwell rules
        have a stable per-subject identity. Idempotent: if a detection
        already carries ``track_id``, the tracker is skipped for it via the
        guard in ``process_frame``.

        C-2 fix: acquires the per-camera threading.Lock so this capture-thread
        call is correctly serialised against process_frame (event-loop thread).
        """
        with self._get_camera_lock(camera_id):
            tracker = self._get_tracker(camera_id)
            tagged = tracker.update(detections)
            self._propagate_person_track_ids(camera_id, tagged)
            return tagged

    def _propagate_person_track_ids(self, camera_id: str, detections: List[Dict]) -> None:
        """Match each PPE detection's ``person_box`` to a currently-tracked
        ``person`` track and write ``person_track_id`` onto the detection.

        Uses the camera's IoU tracker state (already updated by ``tag_tracks``)
        so we don't need a second pass through YOLO. The match is by IoU
        against the live person tracks; ties broken by highest IoU.
        """
        tracker = self._trackers.get(camera_id)
        if not tracker:
            return
        person_tracks = [
            (tid, t["box"]) for tid, t in tracker.tracks.items()
            if t.get("label") == "person" and t.get("missing", 0) == 0
        ]
        if not person_tracks:
            # Synthesize person tracks from `person_box` annotations on PPE
            # detections so two PPE boxes inside the same person crop share a
            # `person_track_id`. We hash the rounded person_box coords as a
            # per-frame group id; not a real tracker (no temporal stability)
            # but enough to group within one frame.
            for d in detections:
                pb = d.get("person_box")
                if not pb:
                    continue
                d["person_track_id"] = (
                    f"p:{int(pb['xmin'])//8}:{int(pb['ymin'])//8}:"
                    f"{int(pb['xmax'])//8}:{int(pb['ymax'])//8}"
                )
            return
        for d in detections:
            pb = d.get("person_box")
            if not pb:
                continue
            target = [pb["xmin"], pb["ymin"], pb["xmax"], pb["ymax"]]
            best_id, best_iou = None, 0.0
            for tid, tbox in person_tracks:
                iou = tracker._calculate_iou(target, tbox)
                if iou > best_iou:
                    best_iou, best_id = iou, tid
            if best_id is not None and best_iou > 0.3:
                d["person_track_id"] = best_id

    async def process_frame(self, camera_id: str, detections: List[Dict], violation_rules: List['ViolationRule']) -> List[Dict]:
        """
        Updates states and returns a list of violation payloads that should be sent to the API.

        C-2 + I-2 fix: uses a per-camera threading.Lock instead of a single
        global asyncio.Lock.  Per-camera locks mean cameras no longer block
        each other (I-2).  threading.Lock correctly serialises this event-loop
        thread against the capture thread that calls tag_tracks (C-2).  Since
        process_frame has no ``await`` points, the synchronous ``with`` context
        manager runs without yielding the event loop.
        """
        with self._get_camera_lock(camera_id):
            tracker = self._get_tracker(camera_id)
            camera_states = self._get_camera_states(camera_id)

            # 1. Update tracker (inject track_id). If callers already invoked
            # tag_tracks(), every detection has a track_id and the tracker state
            # is already up to date — don't run a second update or we'd double-
            # count missing frames and break the soft expiry window.
            already_tracked = bool(detections) and all("track_id" in d for d in detections)
            if already_tracked:
                tracked_detections = detections
            else:
                tracked_detections = tracker.update(detections)

            # 2. Track which (track_id, rule_id) pairs we saw this frame
            seen_this_frame = set()

            results_to_post = []
            now = time.time()

            # D-7 fix: index rules by model_identifier ONCE per frame instead
            # of doing a linear scan inside the per-detection loop.  Reduces
            # the inner loop from O(detections × rules) to O(detections + rules).
            rules_by_model: Dict[Optional[str], List] = {}
            for r in violation_rules:
                rules_by_model.setdefault(r.model_identifier, []).append(r)

            # 3. Process each detection
            for det in tracked_detections:
                tid = det["track_id"]
                model_id = det.get("source_model")
                label = det["label"].lower()

                # Find matching rules for this specific detection (D-7: O(1) bucket lookup)
                bucket = rules_by_model.get(model_id, ())
                matching_rules = [
                    r for r in bucket
                    # If trigger_labels is empty, treat it as a wildcard (match all labels from this model)
                    if not r.trigger_labels or label in r.trigger_labels
                ]

                if not matching_rules:
                    # This detection doesn't match any configured rules for this camera
                    continue

                for rule in matching_rules:
                    sop_id = rule.sop_violation_type_id
                    state_key = (tid, sop_id)
                    seen_this_frame.add(state_key)

                    if state_key not in camera_states:
                        # Enforce LRU cap BEFORE inserting so we never exceed it.
                        if len(camera_states) >= self.max_states_per_camera:
                            self._evict_oldest(camera_states)
                        camera_states[state_key] = {
                            "state": self.STATE_PENDING,
                            "frames_seen": 1,
                            "frames_missing": 0,
                            "last_trigger_at": now,  # used as recency for LRU eviction
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
                            threshold = self._cooldown_thresholds.get(v_type, self._default_cooldown_seconds)
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
                        else:
                            # P1 fix #2: a Pending state that bounces in/out of
                            # detection (seen-miss-seen-miss) would otherwise keep
                            # its `frames_seen` accumulator and either fire a stale
                            # violation after the human has left, or persist as a
                            # zombie entry. Reset the seen counter on any miss so
                            # the hysteresis requirement is `entry_hysteresis`
                            # consecutive observations, not cumulative.
                            s["frames_seen"] = 0

                    elif s["state"] == self.STATE_COOLDOWN:
                        v_type = s["type"]
                        threshold = self._cooldown_thresholds.get(v_type, self._default_cooldown_seconds)
                        # I-4 fix: evict using wall-clock time rather than a
                        # hard-coded 1000-frame count.  1000 frames at 1 FPS is
                        # 16+ minutes; at 30 FPS it's 33 s.  Instead, evict when
                        # the subject has been absent AND 2× the cooldown window
                        # has elapsed.  This bounds the state-dict lifetime to
                        # ~2×cooldown_seconds regardless of per-camera FPS.
                        if s["frames_missing"] > 0 and now - s["last_trigger_at"] > threshold * 2:
                            to_delete.append(state_key)

            # 5. Apply deletions (deferred so we don't mutate dict mid-iteration)
            for k in to_delete:
                camera_states.pop(k, None)

            return results_to_post

    def _evict_oldest(self, camera_states: Dict) -> None:
        """Evict the entry with the smallest ``last_trigger_at``. Pending
        states use the creation timestamp; Active/Cooldown use the last
        observation time. Prefers evicting Pending over Active so we never
        drop an in-flight violation."""
        if not camera_states:
            return
        # First try to evict the oldest Pending entry
        pending = [(k, v) for k, v in camera_states.items() if v["state"] == self.STATE_PENDING]
        if pending:
            victim = min(pending, key=lambda kv: kv[1].get("last_trigger_at", 0))
            del camera_states[victim[0]]
            return
        # Otherwise fall back to oldest by last_trigger_at across all states
        victim_key = min(camera_states, key=lambda k: camera_states[k].get("last_trigger_at", 0))
        del camera_states[victim_key]

    def _create_payload(self, det: Dict, camera_id: str, status: str, sop_id: str, model_identifier: str) -> Dict:
        """Helper to create the expected payload for the Violation API.

        Audit FIX #9: ``Metadata`` is a deep copy of ``det`` so callers that
        keep mutating their detection dict (e.g. drawing annotations or
        rewriting boxes for the next frame) cannot retroactively corrupt a
        violation payload that has already been queued for POST.
        """
        det_copy = copy.deepcopy(det)
        return {
            "TrackId": det_copy["track_id"],
            "Label": det_copy["label"],
            "Score": det_copy["score"],
            "Box": det_copy["box"],
            "StateStatus": status,  # "New" or "Update"
            "SopViolationTypeId": sop_id,
            "ModelIdentifier": model_identifier,
            "Metadata": det_copy,
        }

    def update_settings(self, settings: Dict[str, int]):
        """Update cooldown thresholds dynamically."""
        self._cooldown_thresholds.update(settings)
        logger.info("ViolationManager settings updated: %s", self._cooldown_thresholds)

    def reset_camera(self, camera_id: str) -> None:
        """C-3 fix: drop all tracker and violation state for a camera.

        Called by the RTSP client when a stream reconnects after a disconnect.
        Without this, ``SimpleIouTracker.tracks`` and ``ViolationManager._states``
        retain track-ids from before the disconnect, which can:
          * keep Cooldown entries alive for minutes after the subject is gone,
          * leak Active states through the LRU cap on busy cameras,
          * cause new detections to land on stale ``(track_id, sop_id)`` pairs.

        Idempotent.  Holds the per-camera lock so an in-flight ``process_frame``
        on the event-loop thread can't race the reset.
        """
        with self._get_camera_lock(camera_id):
            self._trackers.pop(camera_id, None)
            self._states.pop(camera_id, None)
        logger.info("[%s] ViolationManager state reset (reconnect / forced).", camera_id)

