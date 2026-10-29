"""
inference/dual_head_reid.py
===========================
Dual-Head Whole-Body Human Re-ID Module.

Implements the architectural roadmap specified in Section 5 of the system plan:
1. Facial Feature Head (dlib / MobileFaceNet, min 60x60 px)
2. Body Appearance Head (extracts 128-d normalized appearance vector from torso & clothing)
3. Weighted Similarity Fusion:
   Final Score = 0.65 * Face_Score + 0.35 * Body_Appearance_Score
4. Track Appearance Memory:
   Sustains identity when staff turn away from the camera, look down at workstations,
   or wear food/pharma SOP face masks and hairnets, eliminating false-positive
   unauthorized access violations.
"""

from __future__ import annotations

import logging
import os
import threading
import time
from typing import Any, Dict, List, Optional, Tuple

import cv2
import numpy as np

import config as _vis_config

logger = logging.getLogger("vision-service.dual_head_reid")

EMBEDDING_DIM = 128
DEFAULT_BODY_MATCH_THRESHOLD = getattr(_vis_config, "DUAL_HEAD_BODY_MATCH_THRESHOLD", 0.78)
DEFAULT_FACE_WEIGHT = getattr(_vis_config, "DUAL_HEAD_FACE_WEIGHT", 0.65)
DEFAULT_BODY_WEIGHT = getattr(_vis_config, "DUAL_HEAD_BODY_WEIGHT", 0.35)
TRACK_APPEARANCE_TTL_SECONDS = 60.0
TRACK_EMA_ALPHA = 0.25


def cosine_similarity(vec1: Optional[np.ndarray], vec2: Optional[np.ndarray]) -> float:
    """
    Compute cosine similarity between two 1D feature vectors.
    Returns float in [-1.0, 1.0], or 0.0 if either vector is empty/invalid.
    """
    if vec1 is None or vec2 is None:
        return 0.0
    v1 = np.asarray(vec1, dtype=np.float32).ravel()
    v2 = np.asarray(vec2, dtype=np.float32).ravel()
    if v1.size == 0 or v2.size == 0 or v1.size != v2.size:
        return 0.0
    n1 = float(np.linalg.norm(v1))
    n2 = float(np.linalg.norm(v2))
    if n1 < 1e-7 or n2 < 1e-7:
        return 0.0
    dot = float(np.dot(v1, v2))
    return float(np.clip(dot / (n1 * n2), -1.0, 1.0))


def fuse_identity_scores(
    face_score: Optional[float],
    body_score: Optional[float],
    face_weight: float = DEFAULT_FACE_WEIGHT,
    body_weight: float = DEFAULT_BODY_WEIGHT,
) -> float:
    """
    Fuse facial similarity and body appearance scores according to Section 5:
    Final Score = 0.65 * Face_Score + 0.35 * Body_Appearance_Score (when both present).
    Gracefully scales if only one head is available.
    """
    if face_score is not None and body_score is not None:
        tot = face_weight + body_weight
        if tot <= 0:
            return 0.0
        return float((face_score * face_weight + body_score * body_weight) / tot)
    if face_score is not None:
        return float(face_score)
    if body_score is not None:
        return float(body_score)
    return 0.0


class BodyAppearanceExtractor:
    """
    Extracts a 128-dimensional L2-normalized appearance vector from a person crop.
    
    Operates in two modes:
    1. Fast multi-zone spatial color-texture descriptors (NumPy + OpenCV, <1ms CPU).
       Divides person crop into upper torso, lower body, and core torso zones.
       Computes HSV histograms, Lab color moments, and spatial gradient energy.
    2. Deep Re-ID model (e.g. OSNet / MobileNet ONNX), if onnxruntime is installed
       and BODY_REID_ONNX_PATH is specified.
    """

    def __init__(self, onnx_model_path: Optional[str] = None):
        self._onnx_path = onnx_model_path or getattr(_vis_config, "BODY_REID_ONNX_PATH", None)
        self._onnx_session = None
        self._onnx_init_attempted = False
        self._lock = threading.Lock()

    def _get_onnx_session(self):
        if self._onnx_init_attempted:
            return self._onnx_session
        with self._lock:
            if self._onnx_init_attempted:
                return self._onnx_session
            self._onnx_init_attempted = True
            if self._onnx_path and os.path.exists(self._onnx_path):
                try:
                    import onnxruntime as ort
                    opts = ort.SessionOptions()
                    opts.intra_op_num_threads = 2
                    opts.inter_op_num_threads = 1
                    opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
                    self._onnx_session = ort.InferenceSession(self._onnx_path, sess_options=opts)
                    logger.info("Loaded Body Re-ID ONNX model from %s", self._onnx_path)
                except Exception as exc:
                    logger.warning("Failed to initialize ONNX Re-ID session from %s: %s", self._onnx_path, exc)
                    self._onnx_session = None
        return self._onnx_session

    def extract(self, person_crop: np.ndarray) -> Optional[np.ndarray]:
        """
        Extract a 128-d normalized appearance vector from an RGB person crop.
        """
        if person_crop is None or not isinstance(person_crop, np.ndarray):
            return None
        h, w = person_crop.shape[:2]
        if h < 20 or w < 10 or person_crop.size == 0:
            return None

        # Try ONNX session if available
        session = self._get_onnx_session()
        if session is not None:
            try:
                emb = self._extract_onnx(person_crop, session)
                if emb is not None:
                    return emb
            except Exception as exc:
                logger.debug("ONNX Re-ID extraction failed: %s; falling back to spatial extractor", exc)

        return self._extract_spatial_multizone(person_crop)

    def _extract_onnx(self, crop: np.ndarray, session: Any) -> Optional[np.ndarray]:
        # Resize to standard Re-ID dimension: 256 height x 128 width
        img = cv2.resize(crop, (128, 256), interpolation=cv2.INTER_LINEAR)
        img = img.astype(np.float32) / 255.0
        # ImageNet normalization
        mean = np.array([0.485, 0.456, 0.406], dtype=np.float32)
        std = np.array([0.229, 0.224, 0.225], dtype=np.float32)
        img = (img - mean) / std
        # HWC -> CHW -> NCHW
        img = np.transpose(img, (2, 0, 1))
        img = np.expand_dims(img, axis=0)

        input_name = session.get_inputs()[0].name
        outputs = session.run(None, {input_name: img})
        raw_emb = outputs[0].ravel()

        # If dimension differs from 128, pool/project or slice
        if raw_emb.size > EMBEDDING_DIM:
            # Reshape and average-pool to 128
            pool_factor = raw_emb.size // EMBEDDING_DIM
            pooled = raw_emb[: EMBEDDING_DIM * pool_factor].reshape(EMBEDDING_DIM, pool_factor).mean(axis=1)
            raw_emb = pooled
        elif raw_emb.size < EMBEDDING_DIM:
            padded = np.zeros(EMBEDDING_DIM, dtype=np.float32)
            padded[: raw_emb.size] = raw_emb
            raw_emb = padded

        norm = float(np.linalg.norm(raw_emb))
        if norm > 1e-7:
            raw_emb = raw_emb / norm
        return raw_emb.astype(np.float32)

    def _extract_spatial_multizone(self, crop: np.ndarray) -> np.ndarray:
        """
        Fast CPU-friendly spatial multi-zone color-texture descriptor (128 dimensions).
        Zones:
        - Zone 1: Upper Torso / Uniform (y in [0.15, 0.60]) -> 44 features
        - Zone 2: Lower Body / Legs (y in [0.60, 0.95])    -> 44 features
        - Zone 3: Core Torso Center (y in [0.20, 0.80], x in [0.20, 0.80]) -> 40 features
        Total: 44 + 44 + 40 = 128 features, unit L2-normalized.
        """
        # Standardize working crop size
        target_h, target_w = 256, 128
        img = cv2.resize(crop, (target_w, target_h), interpolation=cv2.INTER_LINEAR)

        # Convert color spaces
        hsv = cv2.cvtColor(img, cv2.COLOR_RGB2HSV)
        lab = cv2.cvtColor(img, cv2.COLOR_RGB2LAB)
        gray = cv2.cvtColor(img, cv2.COLOR_RGB2GRAY)

        # Zone 1: Upper Torso (lines 38 to 153 of 256)
        y1_a, y1_b = int(target_h * 0.15), int(target_h * 0.60)
        z1_hsv = hsv[y1_a:y1_b, :]
        z1_lab = lab[y1_a:y1_b, :]
        z1_gray = gray[y1_a:y1_b, :]
        f1 = self._compute_zone_features_44(z1_hsv, z1_lab, z1_gray)

        # Zone 2: Lower Body (lines 153 to 243 of 256)
        y2_a, y2_b = int(target_h * 0.60), int(target_h * 0.95)
        z2_hsv = hsv[y2_a:y2_b, :]
        z2_lab = lab[y2_a:y2_b, :]
        z2_gray = gray[y2_a:y2_b, :]
        f2 = self._compute_zone_features_44(z2_hsv, z2_lab, z2_gray)

        # Zone 3: Core Torso Center (focusing on uniform chest / center)
        y3_a, y3_b = int(target_h * 0.20), int(target_h * 0.80)
        x3_a, x3_b = int(target_w * 0.20), int(target_w * 0.80)
        z3_hsv = hsv[y3_a:y3_b, x3_a:x3_b]
        z3_lab = lab[y3_a:y3_b, x3_a:x3_b]
        z3_gray = gray[y3_a:y3_b, x3_a:x3_b]
        f3 = self._compute_zone_features_40(z3_hsv, z3_lab, z3_gray)

        combined = np.concatenate([f1, f2, f3], axis=0).astype(np.float32)
        norm = float(np.linalg.norm(combined))
        if norm > 1e-7:
            combined = combined / norm
        return combined

    @staticmethod
    def _compute_zone_features_44(hsv: np.ndarray, lab: np.ndarray, gray: np.ndarray) -> np.ndarray:
        # 1. Color histograms: 16 Hue, 8 Saturation, 8 Value = 32
        h_hist = cv2.calcHist([hsv], [0], None, [16], [0, 180]).ravel()
        s_hist = cv2.calcHist([hsv], [1], None, [8], [0, 256]).ravel()
        v_hist = cv2.calcHist([hsv], [2], None, [8], [0, 256]).ravel()
        color_hist = np.concatenate([h_hist, s_hist, v_hist], axis=0)
        h_norm = float(np.linalg.norm(color_hist))
        if h_norm > 1e-7:
            color_hist = color_hist / h_norm

        # 2. Lab color moments: mean & std for L, a, b = 6
        l_chan = lab[:, :, 0].astype(np.float32)
        a_chan = lab[:, :, 1].astype(np.float32)
        b_chan = lab[:, :, 2].astype(np.float32)
        moments = np.array([
            float(np.mean(l_chan)) / 255.0,
            float(np.std(l_chan)) / 128.0,
            float(np.mean(a_chan)) / 255.0,
            float(np.std(a_chan)) / 128.0,
            float(np.mean(b_chan)) / 255.0,
            float(np.std(b_chan)) / 128.0,
        ], dtype=np.float32)

        # 3. Spatial gradient energy: horizontal & vertical Sobel moments = 6
        sobel_x = cv2.Sobel(gray, cv2.CV_32F, 1, 0, ksize=3)
        sobel_y = cv2.Sobel(gray, cv2.CV_32F, 0, 1, ksize=3)
        mag = cv2.magnitude(sobel_x, sobel_y)
        grad_feats = np.array([
            float(np.mean(mag)) / 255.0,
            float(np.std(mag)) / 128.0,
            float(np.mean(np.abs(sobel_x))) / 255.0,
            float(np.std(sobel_x)) / 128.0,
            float(np.mean(np.abs(sobel_y))) / 255.0,
            float(np.std(sobel_y)) / 128.0,
        ], dtype=np.float32)

        # 32 + 6 + 6 = 44 features
        return np.concatenate([color_hist, moments, grad_feats], axis=0)

    @staticmethod
    def _compute_zone_features_40(hsv: np.ndarray, lab: np.ndarray, gray: np.ndarray) -> np.ndarray:
        # 1. Color histograms: 16 Hue, 8 Saturation, 4 Value = 28
        h_hist = cv2.calcHist([hsv], [0], None, [16], [0, 180]).ravel()
        s_hist = cv2.calcHist([hsv], [1], None, [8], [0, 256]).ravel()
        v_hist = cv2.calcHist([hsv], [2], None, [4], [0, 256]).ravel()
        color_hist = np.concatenate([h_hist, s_hist, v_hist], axis=0)
        h_norm = float(np.linalg.norm(color_hist))
        if h_norm > 1e-7:
            color_hist = color_hist / h_norm

        # 2. Lab moments = 6
        l_chan = lab[:, :, 0].astype(np.float32)
        a_chan = lab[:, :, 1].astype(np.float32)
        b_chan = lab[:, :, 2].astype(np.float32)
        moments = np.array([
            float(np.mean(l_chan)) / 255.0,
            float(np.std(l_chan)) / 128.0,
            float(np.mean(a_chan)) / 255.0,
            float(np.std(a_chan)) / 128.0,
            float(np.mean(b_chan)) / 255.0,
            float(np.std(b_chan)) / 128.0,
        ], dtype=np.float32)

        # 3. Spatial gradient energy = 6
        sobel_x = cv2.Sobel(gray, cv2.CV_32F, 1, 0, ksize=3)
        sobel_y = cv2.Sobel(gray, cv2.CV_32F, 0, 1, ksize=3)
        mag = cv2.magnitude(sobel_x, sobel_y)
        grad_feats = np.array([
            float(np.mean(mag)) / 255.0,
            float(np.std(mag)) / 128.0,
            float(np.mean(np.abs(sobel_x))) / 255.0,
            float(np.std(sobel_x)) / 128.0,
            float(np.mean(np.abs(sobel_y))) / 255.0,
            float(np.std(sobel_y)) / 128.0,
        ], dtype=np.float32)

        # 28 + 6 + 6 = 40 features
        return np.concatenate([color_hist, moments, grad_feats], axis=0)


class TrackAppearanceMemory:
    """
    Thread-safe Track Appearance Memory.
    Associates (camera_id, track_id) with confirmed identities and dynamic appearance vectors.
    """

    def __init__(
        self,
        ttl_seconds: float = TRACK_APPEARANCE_TTL_SECONDS,
        ema_alpha: float = TRACK_EMA_ALPHA,
        max_tracks_per_camera: int = 500,
    ):
        self._ttl = float(ttl_seconds)
        self._alpha = float(ema_alpha)
        self._max_tracks = int(max_tracks_per_camera)
        # camera_id -> {track_id -> dict}
        self._store: Dict[str, Dict[int, dict]] = {}
        self._lock = threading.RLock()

    def record_face_identity(
        self,
        camera_id: str,
        track_id: Optional[int],
        identity_dict: dict,
        body_vector: Optional[np.ndarray] = None,
    ) -> None:
        """
        Record or update the identity of a track confirmed via the facial recognition head.
        Smooths the body appearance baseline using Exponential Moving Average (EMA).
        """
        if not camera_id or track_id is None:
            return
        try:
            track_id = int(track_id)
            if track_id < 0:
                return
        except (TypeError, ValueError):
            return

        now = time.monotonic()
        with self._lock:
            cam_map = self._store.setdefault(camera_id, {})
            existing = cam_map.get(track_id)

            if existing is not None and body_vector is not None and existing.get("body_vector") is not None:
                # Smooth appearance baseline: (1 - alpha) * old + alpha * new
                old_vec = existing["body_vector"]
                new_vec = (1.0 - self._alpha) * old_vec + self._alpha * body_vector
                n = float(np.linalg.norm(new_vec))
                if n > 1e-7:
                    new_vec = (new_vec / n).astype(np.float32)
                cur_body = new_vec
            elif body_vector is not None:
                cur_body = body_vector.copy().astype(np.float32)
            elif existing is not None:
                cur_body = existing.get("body_vector")
            else:
                cur_body = None

            entry = {
                "employeeId": identity_dict.get("employeeId"),
                "isUnauthorized": bool(identity_dict.get("isUnauthorized", False)),
                "unknownPersonId": identity_dict.get("unknownPersonId"),
                "body_vector": cur_body,
                "last_seen": now,
                "expires_at": now + self._ttl,
                "hits": existing["hits"] + 1 if existing else 1,
            }

            # Enforce capacity
            if len(cam_map) >= self._max_tracks and track_id not in cam_map:
                stale = [t for t, d in cam_map.items() if d["expires_at"] <= now]
                for t in stale:
                    cam_map.pop(t, None)
                if len(cam_map) >= self._max_tracks:
                    oldest_t = min(cam_map.keys(), key=lambda k: cam_map[k]["last_seen"])
                    cam_map.pop(oldest_t, None)

            cam_map[track_id] = entry

    def lookup_body_identity(
        self,
        camera_id: str,
        track_id: Optional[int],
        body_vector: np.ndarray,
        threshold: float = DEFAULT_BODY_MATCH_THRESHOLD,
    ) -> Optional[dict]:
        """
        Attempt to resolve identity using the body appearance head when the facial
        head is occluded, masked, or turned away.
        """
        if not camera_id or body_vector is None:
            return None

        now = time.monotonic()
        with self._lock:
            cam_map = self._store.get(camera_id)
            if not cam_map:
                return None

            # 1. Direct match for track_id
            if track_id is not None:
                try:
                    t_int = int(track_id)
                    entry = cam_map.get(t_int)
                    if entry and entry["expires_at"] > now and entry.get("body_vector") is not None:
                        sim = cosine_similarity(body_vector, entry["body_vector"])
                        if sim >= threshold:
                            # Update EMA baseline and expiration
                            old_vec = entry["body_vector"]
                            new_vec = (1.0 - self._alpha) * old_vec + self._alpha * body_vector
                            n = float(np.linalg.norm(new_vec))
                            if n > 1e-7:
                                entry["body_vector"] = (new_vec / n).astype(np.float32)
                            entry["last_seen"] = now
                            entry["expires_at"] = now + self._ttl
                            entry["hits"] += 1
                            return {
                                "employeeId": entry["employeeId"],
                                "isUnauthorized": entry["isUnauthorized"],
                                "unknownPersonId": entry.get("unknownPersonId"),
                                "matchSource": "body_appearance_sustained",
                                "confidence": sim,
                            }
                except (TypeError, ValueError):
                    pass

            # 2. Re-identification across dropped/reassigned tracks on the same camera
            # (e.g. ByteTrack temporarily dropped track ID within last 5 seconds)
            best_cand = None
            best_sim = threshold + 0.05  # Require higher confidence for cross-track association
            for tid, entry in cam_map.items():
                if entry["expires_at"] > now and (now - entry["last_seen"]) <= 5.0 and entry.get("body_vector") is not None:
                    sim = cosine_similarity(body_vector, entry["body_vector"])
                    if sim > best_sim:
                        best_sim = sim
                        best_cand = (tid, entry)

            if best_cand is not None:
                src_tid, entry = best_cand
                logger.info(
                    "[%s] Re-associated dropped track %s to track %s via body appearance (sim=%.3f)",
                    camera_id,
                    track_id,
                    src_tid,
                    best_sim,
                )
                if track_id is not None:
                    try:
                        self.record_face_identity(camera_id, int(track_id), entry, body_vector)
                    except (TypeError, ValueError):
                        pass
                return {
                    "employeeId": entry["employeeId"],
                    "isUnauthorized": entry["isUnauthorized"],
                    "unknownPersonId": entry.get("unknownPersonId"),
                    "matchSource": "body_appearance_reassociated",
                    "confidence": best_sim,
                }

        return None

    def get_track(self, camera_id: str, track_id: int) -> Optional[dict]:
        if not camera_id or track_id is None:
            return None
        with self._lock:
            cam_map = self._store.get(camera_id)
            if cam_map:
                entry = cam_map.get(int(track_id))
                if entry and entry["expires_at"] > time.monotonic():
                    return dict(entry)
        return None

    def clear_camera(self, camera_id: str) -> int:
        if not camera_id:
            return 0
        with self._lock:
            cam_map = self._store.pop(camera_id, None)
            return len(cam_map) if cam_map else 0

    def clear_all(self) -> None:
        with self._lock:
            self._store.clear()


# Global singletons
body_appearance_extractor = BodyAppearanceExtractor()
track_appearance_memory = TrackAppearanceMemory()
