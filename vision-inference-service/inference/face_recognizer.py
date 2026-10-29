import os
import requests
import requests.adapters
import logging
import threading
import time
import uuid
from PIL import Image
import numpy as np

import config as _vis_config

logger = logging.getLogger("vision-service.face_recognizer")

try:
    import face_recognition
    HAS_FACE_RECOGNITION = True
except ImportError:
    HAS_FACE_RECOGNITION = False
    logger.warning("face_recognition library not installed. Facial recognition is disabled.")

try:
    from inference.dual_head_reid import (
        body_appearance_extractor,
        track_appearance_memory,
        fuse_identity_scores,
        cosine_similarity,
    )
    HAS_DUAL_HEAD = True
except Exception as _dh_err:
    HAS_DUAL_HEAD = False
    logger.warning("Dual-head ReID module failed to import: %s", _dh_err)

# M1/M13 fix: read everything via the central config module so behaviour is
# uniform with the rest of the service. The old in-module ``os.getenv``
# block hid these knobs from log_config() and broke monkeypatch-based tests.
REID_URL = _vis_config.HUMAN_REID_URL
logger.info("face_recognizer using REID_URL=%s", REID_URL)

REID_MATCH_THRESHOLD = _vis_config.HUMAN_REID_MATCH_THRESHOLD
REID_KNOWN_MIN_MARGIN = _vis_config.HUMAN_REID_KNOWN_MIN_MARGIN
REID_TIMEOUT_SECONDS = _vis_config.HUMAN_REID_TIMEOUT_SECONDS
UNKNOWN_REID_THRESHOLD = _vis_config.UNKNOWN_REID_THRESHOLD
UNKNOWN_ID_PREFIX = _vis_config.UNKNOWN_ID_PREFIX
ENABLE_UNKNOWN_REID_TRACKING = _vis_config.ENABLE_UNKNOWN_REID_TRACKING

# M15 fix: a single pooled requests.Session reused across calls keeps the
# TCP/TLS connection alive between thread-pool workers, cutting per-call
# latency to the ReID service from ~30 ms to ~3 ms under load. Thread-safe
# for concurrent use according to the requests docs.
# R5 fix: the default HTTPAdapter keeps only 10 pooled connections; under
# many concurrent camera workers the extras are discarded and re-opened per
# call. Raise pool_maxsize so the pool covers the thread-pool width.
_REID_SESSION = requests.Session()
_REID_ADAPTER = requests.adapters.HTTPAdapter(pool_connections=8, pool_maxsize=32)
_REID_SESSION.mount("http://", _REID_ADAPTER)
_REID_SESSION.mount("https://", _REID_ADAPTER)

# R5 fix: cap unknown-embedding enrollment. Without a cap, a busy doorway of
# never-matching faces enrolls a new "unknown" vector on every frame, growing
# the pgvector table unboundedly and degrading every subsequent /search
# (search death spiral). At most one enrollment per camera per interval.
# Read directly from the environment (config.py is owned by another change
# stream); tune via UNKNOWN_ENROLL_MIN_INTERVAL_SECONDS.
UNKNOWN_ENROLL_MIN_INTERVAL_SECONDS: float = float(
    os.environ.get("UNKNOWN_ENROLL_MIN_INTERVAL_SECONDS", "30")
)
_UNKNOWN_ENROLL_LOCK = threading.Lock()
_LAST_UNKNOWN_ENROLL_BY_CAMERA: dict[str, float] = {}


def _unknown_enrollment_allowed(camera_id: str | None) -> bool:
    """Per-process, per-camera rate limit for unknown-person enrollment."""
    key = str(camera_id) if camera_id else "_no_camera_"
    now = time.monotonic()
    with _UNKNOWN_ENROLL_LOCK:
        last = _LAST_UNKNOWN_ENROLL_BY_CAMERA.get(key)
        if last is not None and (now - last) < UNKNOWN_ENROLL_MIN_INTERVAL_SECONDS:
            return False
        _LAST_UNKNOWN_ENROLL_BY_CAMERA[key] = now
        return True


def _post_reid(url: str, **kwargs):
    """Post to ReID using the pooled session in production, but respect
    test monkeypatches that replace ``requests.post``.

    The acceptance suite stubs ``requests.post`` directly. If we always call
    ``_REID_SESSION.post`` those tests bypass the stub and try to hit the real
    network, which is not what we want. In normal runtime the module-level
    function is untouched, so we get the pooled-session path.
    """
    if getattr(requests.post, "__module__", "") != "requests.api":
        return requests.post(url, **kwargs)
    return _REID_SESSION.post(url, **kwargs)


def _normalize_face_locations(face_locations: list | None) -> list[tuple[int, int, int, int]]:
    normalized: list[tuple[int, int, int, int]] = []
    for loc in face_locations or []:
        if len(loc) != 4:
            continue
        top, right, bottom, left = loc
        normalized.append((int(top), int(right), int(bottom), int(left)))
    return normalized


_FACE_CONCURRENCY = getattr(_vis_config, "FACE_CONCURRENCY_LIMIT", 4)
_FACE_TIMEOUT = getattr(_vis_config, "FACE_SEMAPHORE_TIMEOUT_SECONDS", 1.5)
_FACE_SEMAPHORE = threading.BoundedSemaphore(value=max(1, int(_FACE_CONCURRENCY)))


def _safe_face_locations(image: np.ndarray, model: str = "hog", number_of_times_to_upsample: int = 1) -> list:
    if not _FACE_SEMAPHORE.acquire(blocking=True, timeout=_FACE_TIMEOUT):
        logger.warning(
            "Face detection queue saturated (>%.1fs wait); skipping to maintain stream framerate.",
            _FACE_TIMEOUT,
        )
        return []
    try:
        return face_recognition.face_locations(
            image, model=model, number_of_times_to_upsample=number_of_times_to_upsample
        )
    except Exception as exc:  # noqa: BLE001
        logger.error("face_locations failed: %s", exc)
        return []
    finally:
        _FACE_SEMAPHORE.release()


def _safe_face_encodings(image: np.ndarray, face_locations: list) -> list:
    if not _FACE_SEMAPHORE.acquire(blocking=True, timeout=_FACE_TIMEOUT):
        logger.warning(
            "Face encoding queue saturated (>%.1fs wait); skipping.",
            _FACE_TIMEOUT,
        )
        return []
    try:
        normalized_image = np.ascontiguousarray(image, dtype=np.uint8)
        normalized_locations = _normalize_face_locations(face_locations)
        return face_recognition.face_encodings(
            normalized_image,
            normalized_locations,
            num_jitters=0,
        )
    except Exception as exc:  # noqa: BLE001
        logger.error("face_encodings failed: %s", exc)
        return []
    finally:
        _FACE_SEMAPHORE.release()


def _largest_face_index(face_locations: list) -> int:
    largest_face_idx = 0
    max_area = 0
    for i, loc in enumerate(face_locations):
        top, right, bottom, left = loc
        area = max(0, (bottom - top)) * max(0, (right - left))
        if area > max_area:
            max_area = area
            largest_face_idx = i
    return largest_face_idx


def _is_unknown_person_id(person_id: str | None) -> bool:
    return bool(person_id) and str(person_id).startswith(UNKNOWN_ID_PREFIX)


def _search_reid(tenant_id: str, embedding: list[float], threshold: float, top_k: int = 5) -> list:
    payload = {
        "tenant_id": tenant_id,
        "embedding": embedding,
        "top_k": top_k,
        "threshold": threshold,
    }
    url = f"{REID_URL.rstrip('/')}/search"
    # M15 fix: pooled session reuse in production, while still allowing the
    # acceptance tests to monkeypatch ``requests.post`` directly.
    response = _post_reid(url, json=payload, timeout=REID_TIMEOUT_SECONDS)
    if response.status_code != 200:
        logger.error("ReID search failed: %s", response.text)
        return []
    data = response.json()
    return data if isinstance(data, list) else []


def _store_unknown_embedding(tenant_id: str, embedding: list[float], camera_id: str | None) -> str | None:
    # R5 fix: rate-limit enrollment so unknown sightings can't flood the
    # ReID table (which would slow every subsequent search).
    if not _unknown_enrollment_allowed(camera_id):
        logger.debug(
            "Unknown-embedding enrollment rate-limited for camera %s (min interval %ss).",
            camera_id,
            UNKNOWN_ENROLL_MIN_INTERVAL_SECONDS,
        )
        return None

    unknown_id = f"{UNKNOWN_ID_PREFIX}{uuid.uuid4()}"
    payload = {
        "tenant_id": tenant_id,
        "embedding": embedding,
        "person_id": unknown_id,
        "camera_id": camera_id,
        "metadata_json": {
            "identityKind": "unknown_culprit",
            "source": "vision-inference-service",
        },
    }
    url = f"{REID_URL.rstrip('/')}/embeddings"
    # M15 fix: pooled session reuse in production, while still allowing the
    # acceptance tests to monkeypatch ``requests.post`` directly.
    response = _post_reid(url, json=payload, timeout=REID_TIMEOUT_SECONDS)
    if response.status_code in (200, 201):
        return unknown_id
    logger.warning("Failed storing unknown embedding: HTTP %s %s", response.status_code, response.text)
    return None


def compute_face_embedding(rgb_image: np.ndarray) -> list[float] | None:
    """
    Computes a single 128-d dlib/face_recognition embedding for a clean,
    face-centered image (e.g. a browser enrollment capture) — NOT a full
    surveillance frame with a person body box.

    This exists so face enrollment and live camera recognition (identify_person,
    below) share the exact same embedding model. Enrollment previously computed
    its vector client-side with face-api.js (a different neural network); both
    happen to output 128 numbers, but they are NOT the same embedding space, so
    live dlib-based recognition could never reliably match a face-api.js vector
    regardless of threshold tuning. Returns None if no sufficiently large face
    is found.
    """
    if not HAS_FACE_RECOGNITION:
        return None

    rgb_image = np.ascontiguousarray(rgb_image, dtype=np.uint8)

    face_locations = _safe_face_locations(
        rgb_image, model="hog", number_of_times_to_upsample=1
    )
    if not face_locations:
        logger.debug("compute_face_embedding: no face found in image.")
        return None

    largest_face_idx = _largest_face_index(face_locations)
    top, right, bottom, left = face_locations[largest_face_idx]
    face_h = bottom - top
    face_w = right - left
    # M14 fix parity: reject tiny faces before the expensive encode, same
    # floor identify_person() uses so enrollment and live recognition apply
    # consistent quality standards.
    if face_h < _vis_config.FACE_MIN_DIM_PX or face_w < _vis_config.FACE_MIN_DIM_PX:
        logger.debug("compute_face_embedding: face too small (%dx%d px).", face_w, face_h)
        return None

    encodings = _safe_face_encodings(rgb_image, [face_locations[largest_face_idx]])
    if not encodings:
        return None

    return encodings[0].tolist()


def identify_person(
    rgb_frame: np.ndarray,
    person_box: dict,
    tenant_id: str,
    camera_id: str | None = None,
    track_id: int | None = None,
) -> dict:
    """
    Dual-Head Whole-Body Human Re-ID (Section 5):
    Combines Facial Feature Head (dlib / MobileFaceNet) with Body Appearance Head
    (128-d spatial torso & clothing descriptors) with weighted similarity fusion.

    When facial landmarks are occluded (facing away, head down at workstation,
    or wearing SOP face masks / hairnets), the Body Appearance Head sustains identity
    from the track appearance cache, preventing false-positive unauthorized violations.

    Returns {"employeeId": str|None, "isUnauthorized": bool, "unknownPersonId": str|None}
    """
    try:
        rgb_frame = np.ascontiguousarray(rgb_frame, dtype=np.uint8)
        xmin, ymin, xmax, ymax = person_box["xmin"], person_box["ymin"], person_box["xmax"], person_box["ymax"]

        # Add some padding to the box
        h, w = rgb_frame.shape[:2]
        padding = 20
        xmin_pad = max(0, int(xmin) - padding)
        ymin_pad = max(0, int(ymin) - padding)
        xmax_pad = min(w, int(xmax) + padding)
        ymax_pad = min(h, int(ymax) + padding)

        person_crop = rgb_frame[ymin_pad:ymax_pad, xmin_pad:xmax_pad]

        # 1. Extract Body Appearance Vector (Head 2)
        body_vec = None
        if HAS_DUAL_HEAD and getattr(_vis_config, "DUAL_HEAD_REID_ENABLED", True) and person_crop.size > 0:
            try:
                body_vec = body_appearance_extractor.extract(person_crop)
            except Exception as exc:
                logger.debug("[%s] Body appearance extraction failed: %s", camera_id, exc)

        # 2. Check Facial Feature Head (Head 1)
        face_locations = []
        if HAS_FACE_RECOGNITION and person_crop.size > 0:
            face_locations = _safe_face_locations(
                person_crop, model="hog", number_of_times_to_upsample=1
            )
            # If no face found in initial crop, try a targeted expanded upper-body crop
            if not face_locations:
                box_h = max(1, int(ymax) - int(ymin))
                box_w = max(1, int(xmax) - int(xmin))
                head_ymin = max(0, int(ymin) - int(box_h * 0.25))
                head_ymax = min(h, int(ymin) + int(box_h * 0.65))
                head_xmin = max(0, int(xmin) - int(box_w * 0.20))
                head_xmax = min(w, int(xmax) + int(box_w * 0.20))

                expanded_crop = rgb_frame[head_ymin:head_ymax, head_xmin:head_xmax]
                if expanded_crop.size > 0:
                    face_locations = _safe_face_locations(
                        expanded_crop, model="hog", number_of_times_to_upsample=1
                    )
                    if face_locations:
                        person_crop = expanded_crop

        face_valid = False
        face_loc = None
        if face_locations:
            largest_face_idx = _largest_face_index(face_locations)
            top, right, bottom, left = face_locations[largest_face_idx]
            face_h = bottom - top
            face_w = right - left
            if face_h >= _vis_config.FACE_MIN_DIM_PX and face_w >= _vis_config.FACE_MIN_DIM_PX:
                face_valid = True
                face_loc = face_locations[largest_face_idx]
            else:
                logger.debug("[%s] Crop face too small (%dx%d px); rejecting face head.", camera_id, face_w, face_h)

        # 3. Facial Head Occluded / Missing -> Consult Body Appearance Head
        if not face_valid:
            if HAS_DUAL_HEAD and getattr(_vis_config, "DUAL_HEAD_REID_ENABLED", True) and body_vec is not None and camera_id:
                sustained = track_appearance_memory.lookup_body_identity(
                    camera_id=camera_id,
                    track_id=track_id,
                    body_vector=body_vec,
                    threshold=_vis_config.DUAL_HEAD_BODY_MATCH_THRESHOLD,
                )
                if sustained is not None:
                    logger.info(
                        "[%s] Track %s sustained identity via Body Appearance Head (emp=%s, src=%s, conf=%.3f)",
                        camera_id,
                        track_id,
                        sustained.get("employeeId"),
                        sustained.get("matchSource"),
                        sustained.get("confidence", 0.0),
                    )
                    return {
                        "employeeId": sustained.get("employeeId"),
                        "isUnauthorized": bool(sustained.get("isUnauthorized", False)),
                        "unknownPersonId": sustained.get("unknownPersonId"),
                        "matchSource": sustained.get("matchSource"),
                        "confidence": sustained.get("confidence"),
                    }

            if not HAS_FACE_RECOGNITION:
                return {"employeeId": None, "isUnauthorized": False}

            logger.debug("[%s] No valid face found and no sustained body match for track %s.", camera_id, track_id)
            return {"employeeId": None, "isUnauthorized": True, "unknownPersonId": None}

        # 4. Facial Head Active: Extract 128-d face embedding
        face_encodings = _safe_face_encodings(person_crop, [face_loc])
        if not face_encodings:
            return {"employeeId": None, "isUnauthorized": True, "unknownPersonId": None}

        primary_encoding = face_encodings[0]
        emb = primary_encoding.tolist()

        # Search broadly once, then resolve by identity class
        results = _search_reid(
            tenant_id=tenant_id,
            embedding=emb,
            threshold=min(REID_MATCH_THRESHOLD, UNKNOWN_REID_THRESHOLD),
            top_k=5,
        )

        known_candidates = []
        best_unknown = None
        for item in results:
            person_id = item.get("person_id")
            score = float(item.get("score", 0.0))
            if person_id and not _is_unknown_person_id(person_id):
                logger.info("ReID search candidate: %s (score=%.3f, threshold=%.3f)", person_id, score, REID_MATCH_THRESHOLD)
                if score >= REID_MATCH_THRESHOLD:
                    known_candidates.append({"person_id": person_id, "score": score})
            if person_id and _is_unknown_person_id(person_id) and score >= UNKNOWN_REID_THRESHOLD:
                if best_unknown is None or score > best_unknown.get("score", 0.0):
                    best_unknown = {"person_id": person_id, "score": score}

        resolved_identity = None
        known_candidates.sort(key=lambda x: x["score"], reverse=True)
        if known_candidates:
            top_known = known_candidates[0]
            second_known = known_candidates[1] if len(known_candidates) > 1 else None
            if second_known is not None:
                margin = float(top_known["score"]) - float(second_known["score"])
                if margin < REID_KNOWN_MIN_MARGIN:
                    logger.info(
                        "ReID known match ambiguous (top=%s %.3f, second=%s %.3f, margin=%.3f < %.3f)",
                        top_known["person_id"],
                        float(top_known["score"]),
                        second_known["person_id"],
                        float(second_known["score"]),
                        margin,
                        REID_KNOWN_MIN_MARGIN,
                    )
                else:
                    resolved_identity = {
                        "employeeId": top_known["person_id"],
                        "isUnauthorized": False,
                        "unknownPersonId": None,
                        "score": top_known["score"],
                    }
            else:
                resolved_identity = {
                    "employeeId": top_known["person_id"],
                    "isUnauthorized": False,
                    "unknownPersonId": None,
                    "score": top_known["score"],
                }

        if resolved_identity is None:
            if known_candidates:
                logger.info("ReID known candidates present but unresolved; treating as unknown.")

            if best_unknown is not None:
                resolved_identity = {
                    "employeeId": None,
                    "isUnauthorized": True,
                    "unknownPersonId": best_unknown["person_id"],
                    "score": best_unknown["score"],
                }
            else:
                unknown_person_id = None
                if ENABLE_UNKNOWN_REID_TRACKING:
                    unknown_person_id = _store_unknown_embedding(
                        tenant_id=tenant_id,
                        embedding=emb,
                        camera_id=camera_id,
                    )
                resolved_identity = {
                    "employeeId": None,
                    "isUnauthorized": True,
                    "unknownPersonId": unknown_person_id,
                    "score": 0.0,
                }

        # 5. Dual-Head Score Fusion & Track Appearance Memory update
        if HAS_DUAL_HEAD and getattr(_vis_config, "DUAL_HEAD_REID_ENABLED", True) and camera_id and track_id is not None:
            cached_track = track_appearance_memory.get_track(camera_id, track_id)
            if cached_track and cached_track.get("body_vector") is not None and body_vec is not None:
                body_sim = cosine_similarity(body_vec, cached_track["body_vector"])
                fused = fuse_identity_scores(
                    face_score=resolved_identity.get("score"),
                    body_score=body_sim,
                    face_weight=_vis_config.DUAL_HEAD_FACE_WEIGHT,
                    body_weight=_vis_config.DUAL_HEAD_BODY_WEIGHT,
                )
                resolved_identity["fusedScore"] = fused
                logger.debug("[%s] Track %s dual-head fusion: face=%.3f, body=%.3f -> fused=%.3f", camera_id, track_id, resolved_identity.get("score", 0.0), body_sim, fused)

            track_appearance_memory.record_face_identity(
                camera_id=camera_id,
                track_id=track_id,
                identity_dict=resolved_identity,
                body_vector=body_vec,
            )

        return resolved_identity

    except Exception as e:
        logger.error(f"Facial recognition error: {e}")
        return {"employeeId": None, "isUnauthorized": False, "unknownPersonId": None}

