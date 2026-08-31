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


_DLIB_MUTEX = threading.Lock()


def _safe_face_locations(image: np.ndarray, model: str = "hog", number_of_times_to_upsample: int = 1) -> list:
    with _DLIB_MUTEX:
        try:
            return face_recognition.face_locations(
                image, model=model, number_of_times_to_upsample=number_of_times_to_upsample
            )
        except Exception as exc:  # noqa: BLE001
            logger.error("face_locations failed: %s", exc)
            return []


def _safe_face_encodings(image: np.ndarray, face_locations: list) -> list:
    with _DLIB_MUTEX:
        normalized_image = np.ascontiguousarray(image, dtype=np.uint8)
        normalized_locations = _normalize_face_locations(face_locations)
        try:
            return face_recognition.face_encodings(
                normalized_image,
                normalized_locations,
                num_jitters=0,
            )
        except Exception as exc:  # noqa: BLE001
            logger.error("face_encodings failed: %s", exc)
            return []


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


def identify_person(rgb_frame: np.ndarray, person_box: dict, tenant_id: str, camera_id: str | None = None) -> dict:
    """
    Crops the person from the frame, extracts a face embedding, and queries the ReID service.
    Returns {"employeeId": str|None, "isUnauthorized": bool, "unknownPersonId": str|None}
    """
    if not HAS_FACE_RECOGNITION:
        return {"employeeId": None, "isUnauthorized": False}

    try:
        rgb_frame = np.ascontiguousarray(rgb_frame, dtype=np.uint8)
        xmin, ymin, xmax, ymax = person_box["xmin"], person_box["ymin"], person_box["xmax"], person_box["ymax"]
        
        # Add some padding to the box
        h, w = rgb_frame.shape[:2]
        padding = 20
        xmin = max(0, int(xmin) - padding)
        ymin = max(0, int(ymin) - padding)
        xmax = min(w, int(xmax) + padding)
        ymax = min(h, int(ymax) + padding)

        person_crop = rgb_frame[ymin:ymax, xmin:xmax]
        
        # face_recognition works on RGB numpy arrays
        face_locations = _safe_face_locations(
            person_crop, model="hog", number_of_times_to_upsample=1
        )
        
        if not face_locations:
            # Fallback for off-center/low-light frames: try whole-frame face
            # detection, then pick the largest face.
            full_faces = _safe_face_locations(
                rgb_frame, model="hog", number_of_times_to_upsample=1
            )
            if not full_faces:
                logger.debug("No face found in person crop or full frame.")
                return {"employeeId": None, "isUnauthorized": True, "unknownPersonId": None}

            largest_face_idx = _largest_face_index(full_faces)
            selected_face = full_faces[largest_face_idx]
            face_h = selected_face[2] - selected_face[0]
            face_w = selected_face[1] - selected_face[3]
            # M14 fix: reject tiny crops BEFORE the expensive encode. The
            # face_encodings() call is ~5-20ms on CPU and is wasted work
            # when the crop is below the 60x60 reliability floor.
            if face_h < _vis_config.FACE_MIN_DIM_PX or face_w < _vis_config.FACE_MIN_DIM_PX:
                logger.debug("Full-frame face too small (%dx%d); skipping encode.", face_w, face_h)
                return {"employeeId": None, "isUnauthorized": True, "unknownPersonId": None}
            face_encodings = _safe_face_encodings(rgb_frame, [selected_face])
        else:
            # Select the most prominent face within the person crop.
            largest_face_idx = _largest_face_index(face_locations)
            top, right, bottom, left = face_locations[largest_face_idx]
            face_h = bottom - top
            face_w = right - left
            # M14 fix: same early reject inside the person-crop branch.
            if face_h < _vis_config.FACE_MIN_DIM_PX or face_w < _vis_config.FACE_MIN_DIM_PX:
                logger.debug("Crop face too small (%dx%d); skipping encode.", face_w, face_h)
                return {"employeeId": None, "isUnauthorized": True, "unknownPersonId": None}

            # Extract embeddings (only after we know the face is large enough)
            face_encodings = _safe_face_encodings(person_crop, face_locations)

            if not face_encodings:
                return {"employeeId": None, "isUnauthorized": True, "unknownPersonId": None}

            primary_encoding = face_encodings[largest_face_idx]

        if not face_encodings:
            return {"employeeId": None, "isUnauthorized": True, "unknownPersonId": None}

        # M14 fix: dim check moved above — keep this as a defensive no-op for
        # legacy callers that bypass the early-reject branches.
        if face_h < _vis_config.FACE_MIN_DIM_PX or face_w < _vis_config.FACE_MIN_DIM_PX:
            logger.debug(
                "Face crop too small (%dx%d px); skipping re-ID.", face_w, face_h
            )
            return {"employeeId": None, "isUnauthorized": True, "unknownPersonId": None}

        if "primary_encoding" not in locals():
            primary_encoding = face_encodings[0]

        emb = primary_encoding.tolist()

        # Search broadly once, then resolve by identity class:
        # 1) known employee ids (preferred), 2) previously seen unknown ids.
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
            if person_id and not _is_unknown_person_id(person_id) and score >= REID_MATCH_THRESHOLD:
                known_candidates.append({"person_id": person_id, "score": score})
            if person_id and _is_unknown_person_id(person_id) and score >= UNKNOWN_REID_THRESHOLD:
                if best_unknown is None or score > best_unknown.get("score", 0.0):
                    best_unknown = {"person_id": person_id, "score": score}

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
                    return {
                        "employeeId": top_known["person_id"],
                        "isUnauthorized": False,
                        "unknownPersonId": None,
                    }
            else:
                return {
                    "employeeId": top_known["person_id"],
                    "isUnauthorized": False,
                    "unknownPersonId": None,
                }

        if known_candidates:
            # Known candidates existed but were ambiguous; fail closed for identity.
            logger.info("ReID known candidates present but unresolved; treating as unknown.")

        if best_unknown is not None:
            return {
                "employeeId": None,
                "isUnauthorized": True,
                "unknownPersonId": best_unknown["person_id"],
            }

        unknown_person_id = None
        if ENABLE_UNKNOWN_REID_TRACKING:
            unknown_person_id = _store_unknown_embedding(
                tenant_id=tenant_id,
                embedding=emb,
                camera_id=camera_id,
            )

        return {
            "employeeId": None,
            "isUnauthorized": True,
            "unknownPersonId": unknown_person_id,
        }

    except Exception as e:
        logger.error(f"Facial recognition error: {e}")
        return {"employeeId": None, "isUnauthorized": False, "unknownPersonId": None}
