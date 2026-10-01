"""
rules/ergonomics.py
===================
Real-time Ergonomic Risk & Posture Evaluation Engine.
Implements:
1. Official REBA (Rapid Entire Body Assessment) Framework (Tables A, B, C).
2. Dynamic Joint Angle Tracking from 2D/3D skeletal keypoints.
3. NIOSH Lifting Equation (RWL & Lifting Index calculation).
4. Manual Material Handling (MMH) reach and vertical tier analysis.
"""

from __future__ import annotations

import os
import math
import time
import threading
import logging
from typing import Any, Dict, List, Optional, Tuple

logger = logging.getLogger("vision-service.rules.ergonomics")

# ── OFFICIAL REBA LOOKUP TABLES ──────────────────────────────────────────────
# Table A: [Trunk 1..5][Neck 1..3][Legs 1..4]
TABLE_A = [
    # Trunk 1
    [[1, 2, 3, 4], [2, 3, 4, 5], [2, 4, 5, 6]],
    # Trunk 2
    [[2, 3, 4, 5], [3, 4, 5, 6], [4, 5, 6, 7]],
    # Trunk 3
    [[2, 4, 5, 6], [4, 5, 6, 7], [5, 6, 7, 8]],
    # Trunk 4
    [[3, 5, 6, 7], [5, 6, 7, 8], [6, 7, 8, 9]],
    # Trunk 5
    [[4, 6, 7, 8], [6, 7, 8, 9], [7, 8, 9, 9]],
]

# Table B: [UpperArm 1..6][LowerArm 1..2][Wrist 1..3]
TABLE_B = [
    # Upper Arm 1
    [[1, 2, 2], [1, 2, 3]],
    # Upper Arm 2
    [[1, 2, 3], [2, 3, 4]],
    # Upper Arm 3
    [[3, 4, 5], [4, 5, 5]],
    # Upper Arm 4
    [[4, 5, 5], [5, 6, 7]],
    # Upper Arm 5
    [[6, 7, 8], [7, 8, 8]],
    # Upper Arm 6
    [[7, 8, 8], [8, 9, 9]],
]

# Table C: [Score A 1..12][Score B 1..12]
TABLE_C = [
    [1, 1, 1, 2, 3, 3, 4, 5, 6, 7, 7, 7],
    [1, 2, 2, 3, 4, 4, 5, 6, 6, 7, 7, 8],
    [2, 3, 3, 3, 4, 5, 6, 7, 7, 8, 8, 8],
    [3, 4, 4, 4, 5, 6, 7, 8, 8, 9, 9, 9],
    [4, 4, 5, 6, 7, 7, 8, 9, 9, 10, 10, 11],
    [6, 6, 7, 8, 8, 9, 9, 10, 10, 11, 11, 11],
    [7, 7, 8, 8, 9, 9, 10, 11, 11, 11, 12, 12],
    [8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 12, 12],
    [9, 9, 10, 10, 11, 11, 12, 12, 12, 12, 12, 12],
    [10, 10, 11, 11, 11, 12, 12, 12, 12, 12, 12, 12],
    [11, 11, 11, 12, 12, 12, 12, 12, 12, 12, 12, 12],
    [12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12],
]


def calculate_reba_score(
    trunk: int,
    neck: int,
    legs: int,
    upper_arm: int,
    lower_arm: int,
    wrist: int,
    load_kg: float = 5.0,
    has_object: bool = False,
    activity_score: int = 1,
) -> int:
    """Computes standard REBA score given scored body parts and load."""
    t = min(max(trunk, 1), 5) - 1
    n = min(max(neck, 1), 3) - 1
    l = min(max(legs, 1), 4) - 1

    u = min(max(upper_arm, 1), 6) - 1
    la = min(max(lower_arm, 1), 2) - 1
    w = min(max(wrist, 1), 3) - 1

    # Table A lookup + Load Score
    posture_score_a = TABLE_A[t][n][l]
    load_score = 0
    if 5.0 <= load_kg <= 10.0:
        load_score = 1
    elif load_kg > 10.0:
        load_score = 2
    score_a = posture_score_a + load_score

    # Table B lookup + Coupling Score
    posture_score_b = TABLE_B[u][la][w]
    coupling_score = 1 if has_object else 0
    score_b = posture_score_b + coupling_score

    # Table C lookup + Activity Score
    idx_a = min(max(score_a, 1), 12) - 1
    idx_b = min(max(score_b, 1), 12) - 1
    score_c = TABLE_C[idx_a][idx_b]

    final_reba = score_c + activity_score
    return min(max(final_reba, 1), 15)


def calculate_angle(
    p1: Tuple[float, float],
    p2: Tuple[float, float],
    p3: Tuple[float, float],
) -> float:
    """Computes angle at p2 in degrees formed by (p1 - p2 - p3)."""
    rad = math.atan2(p3[1] - p2[1], p3[0] - p2[0]) - math.atan2(p1[1] - p2[1], p1[0] - p2[0])
    ang = abs(rad * 180.0 / math.pi)
    if ang > 180.0:
        ang = 360.0 - ang
    return ang


COCO_KEYPOINTS = [
    "nose", "left_eye", "right_eye", "left_ear", "right_ear",
    "left_shoulder", "right_shoulder", "left_elbow", "right_elbow",
    "left_wrist", "right_wrist", "left_hip", "right_hip",
    "left_knee", "right_knee", "left_ankle", "right_ankle"
]


def evaluate_pose_ergonomics(
    keypoints: Any,
    frame_width: int,
    frame_height: int,
    actual_weight_kg: float = 8.0,
    profile_gender: str = "Male",
    reba_risk_threshold: int = 7,
) -> Dict[str, Any]:
    """
    Evaluates posture risks, joint angles, REBA score, and NIOSH lifting equation
    from normalized COCO-17 keypoints.

    Keypoints format: { "nose": (x, y, conf), "left_shoulder": (x, y, conf), ... }
    or list/tuple of points: [[x, y, conf], ...]
    """
    if isinstance(keypoints, (list, tuple)):
        keypoints = {
            name: (float(pt[0]), float(pt[1]), float(pt[2]) if len(pt) > 2 else 0.9)
            for name, pt in zip(COCO_KEYPOINTS, keypoints)
            if len(pt) >= 2
        }
    elif not isinstance(keypoints, dict):
        keypoints = {}

    # Select best side based on confidence
    l_shld = keypoints.get("left_shoulder", (0, 0, 0))
    r_shld = keypoints.get("right_shoulder", (0, 0, 0))
    use_left = l_shld[2] >= r_shld[2]

    prefix = "left_" if use_left else "right_"
    shld = keypoints.get(f"{prefix}shoulder", l_shld)
    elbw = keypoints.get(f"{prefix}elbow", keypoints.get("left_elbow", (0, 0, 0)))
    wrst = keypoints.get(f"{prefix}wrist", keypoints.get("left_wrist", (0, 0, 0)))
    hip = keypoints.get(f"{prefix}hip", keypoints.get("left_hip", (0, 0, 0)))
    knee = keypoints.get(f"{prefix}knee", keypoints.get("left_knee", (0, 0, 0)))
    ankle = keypoints.get(f"{prefix}ankle", keypoints.get("left_ankle", (0, 0, 0)))
    nose = keypoints.get("nose", (0, 0, 0))

    # Helper point conversion to pixel space
    def to_px(pt):
        return (pt[0] * frame_width, pt[1] * frame_height)

    p_shld = to_px(shld)
    p_elbw = to_px(elbw)
    p_wrst = to_px(wrst)
    p_hip = to_px(hip)
    p_knee = to_px(knee)
    p_ankle = to_px(ankle)
    p_nose = to_px(nose)

    # Joint angles
    ang_trunk = calculate_angle(p_shld, p_hip, p_knee)
    ang_neck = calculate_angle(p_nose, p_shld, p_hip)
    ang_uarm = calculate_angle(p_hip, p_shld, p_elbw)
    ang_larm = calculate_angle(p_shld, p_elbw, p_wrst)
    ang_legs = calculate_angle(p_hip, p_knee, p_ankle)
    ang_wrist = 180.0 - abs(ang_larm - 90.0)

    # Sub-scores
    trunk_dev = abs(180.0 - ang_trunk)
    if trunk_dev <= 5:
        t_score = 1
    elif trunk_dev <= 20:
        t_score = 2
    elif trunk_dev <= 60:
        t_score = 3
    else:
        t_score = 4

    n_score = 1 if ang_neck <= 20.0 else 2
    if ang_uarm <= 20.0:
        a_score = 1
    elif ang_uarm <= 45.0:
        a_score = 2
    elif ang_uarm <= 90.0:
        a_score = 3
    else:
        a_score = 4

    la_score = 1 if (60.0 <= ang_larm <= 100.0) else 2
    leg_dev = abs(180.0 - ang_legs)
    l_score = 1 if leg_dev <= 30.0 else 2 if leg_dev <= 60.0 else 3
    w_score = 1 if abs(180.0 - ang_wrist) <= 15.0 else 2

    # REBA computation
    reba_score = calculate_reba_score(
        trunk=t_score,
        neck=n_score,
        legs=l_score,
        upper_arm=a_score,
        lower_arm=la_score,
        wrist=w_score,
        load_kg=actual_weight_kg,
        has_object=True,
    )

    # ── NIOSH Lifting Equation ────────────────────────────────────────────────
    # Estimate pixel-to-cm scale from height (avg human height ~ 170cm)
    body_pixel_h = max(100.0, math.hypot(p_nose[0] - p_ankle[0], p_nose[1] - p_ankle[1]))
    scale_cm_per_px = 170.0 / body_pixel_h

    # Horizontal distance H (cm)
    h_pixel = math.hypot(p_wrst[0] - p_ankle[0], p_wrst[1] - p_ankle[1])
    h_cm = max(25.0, min(63.0, h_pixel * scale_cm_per_px))
    hm = min(1.0, 25.0 / h_cm)

    # Vertical height V (cm)
    v_pixel = abs(p_ankle[1] - p_wrst[1])
    v_cm = max(0.0, v_pixel * scale_cm_per_px)
    vm = max(0.0, 1.0 - (0.0033 * abs(v_cm - 75.0)))

    # Distance D (cm)
    d_cm = 25.0
    dm = min(1.0, 0.82 + (4.5 / d_cm))

    # Asymmetry angle A
    am = max(0.0, 1.0 - (0.0032 * trunk_dev))
    fm = 0.95
    cm = 1.0

    rwl = 23.0 * hm * vm * dm * am * fm * cm
    li = actual_weight_kg / max(0.1, rwl)
    niosh_safe = li <= 1.0

    # MMH Zone
    if p_wrst[1] < p_shld[1]:
        mmh_zone = "Above Shoulder"
    elif p_wrst[1] < p_elbw[1]:
        mmh_zone = "Shoulder to Elbow"
    elif p_wrst[1] < p_hip[1]:
        mmh_zone = "Elbow to Knuckle"
    elif p_wrst[1] < p_knee[1]:
        mmh_zone = "Knuckle to Mid-Leg"
    else:
        mmh_zone = "Below Mid-Leg"
    mmh_reach = "Far Reach" if h_cm > 40.0 else "Close Reach"

    # Risk level & reasoning
    if reba_score <= 3:
        risk_level = "Low Risk"
    elif reba_score <= 7:
        risk_level = "Medium Risk"
    elif reba_score <= 10:
        risk_level = "High Risk"
    else:
        risk_level = "Very High Risk"

    reasons = []
    if trunk_dev > 20:
        reasons.append(f"Trunk flexed at {trunk_dev:.1f}°")
    if a_score >= 3:
        reasons.append(f"Upper arm raised at {ang_uarm:.1f}°")
    if n_score > 1:
        reasons.append(f"Neck bent at {ang_neck:.1f}°")
    if not niosh_safe:
        reasons.append(f"NIOSH Lifting Index {li:.2f} (exceeds 1.0 safe limit)")
    if mmh_zone in ("Above Shoulder", "Below Mid-Leg"):
        reasons.append(f"Unfavourable reach tier ({mmh_zone})")

    risk_reason = "; ".join(reasons) if reasons else "Normal ergonomic posture"

    is_violating = reba_score >= reba_risk_threshold or not niosh_safe

    return {
        "is_risk": is_violating,
        "reba_score": reba_score,
        "risk_level": risk_level,
        "risk_reason": risk_reason,
        "joint_angles": {
            "trunk": round(ang_trunk, 1),
            "neck": round(ang_neck, 1),
            "upper_arm": round(ang_uarm, 1),
            "lower_arm": round(ang_larm, 1),
            "legs": round(ang_legs, 1),
            "wrist": round(ang_wrist, 1),
        },
        "part_scores": {
            "trunk": t_score,
            "neck": n_score,
            "upper_arm": a_score,
            "lower_arm": la_score,
            "legs": l_score,
            "wrist": w_score,
        },
        "niosh": {
            "rwl_kg": round(rwl, 2),
            "lifting_index": round(li, 2),
            "is_safe": niosh_safe,
            "horizontal_cm": round(h_cm, 1),
            "vertical_cm": round(v_cm, 1),
        },
        "mmh": {
            "zone": mmh_zone,
            "reach": mmh_reach,
        },
    }


# ── Continuous Awkward Posture Duration Tracker (5.0s / 10.0s Configurable) ──
DEFAULT_ERGO_DURATION_S: float = float(os.environ.get("ERGO_DURATION_SECONDS", "5.0"))

# In-memory store: {store_key: {track_key: (started_at, last_seen_at)}}
_ERGO_STORE: Dict[str, Dict[str, Tuple[float, float]]] = {}
_ERGO_STORE_MUTEX = threading.Lock()


def _ergo_track_key(detection: Dict) -> str:
    """Stable identifier for a person across video frames."""
    tid = detection.get("track_id")
    if tid is not None:
        return f"t:{tid}"
    box = detection.get("box") or {}
    if box:
        cx = int(((box.get("xmin", 0) + box.get("xmax", 0)) / 2) // 30)
        cy = int(((box.get("ymin", 0) + box.get("ymax", 0)) / 2) // 30)
        return f"b:{cx}:{cy}"
    return "worker:0"


def _gc_ergo_expired(store: Dict[str, Tuple[float, float]], duration_s: float, now: float) -> None:
    """Drop tracks not observed for more than 3x the duration window."""
    if not store:
        return
    expiry_window = max(duration_s * 3.0, 30.0)
    stale = [k for k, (_, last_seen) in store.items() if last_seen < now - expiry_window]
    for k in stale:
        store.pop(k, None)


def clear_camera_ergonomics(camera_id: str) -> int:
    """Wipe ergonomics posture duration state for a camera upon reconnect or teardown."""
    if not camera_id:
        return 0
    prefix = f"{camera_id}:"
    with _ERGO_STORE_MUTEX:
        keys = [k for k in _ERGO_STORE if k.startswith(prefix) or k == camera_id]
        for k in keys:
            _ERGO_STORE.pop(k, None)
    if keys:
        logger.debug("ergonomics: cleared %d store entries for camera %s", len(keys), camera_id)
    return len(keys)


def clear_all_ergonomics() -> None:
    """Diagnostic/testing helper to reset all ergonomics tracking stores."""
    with _ERGO_STORE_MUTEX:
        _ERGO_STORE.clear()


def evaluate_ergonomics_rule(
    det: Dict,
    rule_config: Dict,
    frame_size: Optional[Tuple[int, int]] = None,
    camera_id: str = "",
    now: Optional[float] = None,
    is_single_image: bool = False,
) -> bool:
    """
    Evaluates postural risk against the configured rule, enforcing that the
    awkward/high-risk posture is sustained continuously for a configured duration
    (defaults to 10.0 seconds) before triggering a violation.

    If the person straightens up or assumes an ergonomically safe posture, the
    timer is immediately reset.
    """
    keypoints = det.get("keypoints")
    if not keypoints:
        return False

    w, h = frame_size or (1920, 1080)
    reba_thresh = int(rule_config.get("reba_threshold", 7))
    weight_kg = float(rule_config.get("actual_weight_kg", 8.0))
    profile_gender = str(rule_config.get("profile_gender", "Male"))

    raw_duration = rule_config.get("duration_s", rule_config.get("duration_seconds", DEFAULT_ERGO_DURATION_S))
    try:
        duration_s = float(raw_duration)
    except (TypeError, ValueError):
        duration_s = DEFAULT_ERGO_DURATION_S

    ergo_result = evaluate_pose_ergonomics(
        keypoints=keypoints,
        frame_width=w,
        frame_height=h,
        actual_weight_kg=weight_kg,
        profile_gender=profile_gender,
        reba_risk_threshold=reba_thresh,
    )

    is_risk = bool(ergo_result.get("is_risk"))
    track_key = _ergo_track_key(det)
    store_key = f"{camera_id}:{reba_thresh}" if camera_id else "global_camera"

    if now is None:
        now = time.monotonic()

    with _ERGO_STORE_MUTEX:
        if store_key not in _ERGO_STORE:
            _ERGO_STORE[store_key] = {}
        store = _ERGO_STORE[store_key]

        if not is_risk:
            # Person straightened up / returned to normal safe posture!
            # Immediately clear accumulated timer so next posture risk must be held for 10s again.
            store.pop(track_key, None)
            _gc_ergo_expired(store, duration_s, now)
            return False

        # Person is in an awkward/high-risk posture.
        # Bypass duration for single static image analysis or if duration_s <= 0
        if is_single_image or duration_s <= 0.0 or rule_config.get("bypass_duration") is True or camera_id.startswith("analyze:image"):
            det["ergonomics"] = ergo_result
            det["ergonomics"]["sustained_duration_s"] = 0.0
            det["ergonomics"]["required_duration_s"] = duration_s
            det["risk_reason"] = ergo_result.get("risk_reason", "High ergonomic posture risk")
            det["is_ergonomic"] = True
            return True

        # Video stream: evaluate sustained duration
        entry = store.get(track_key)
        if entry is None:
            # First frame observed in risk posture: arm the duration timer
            store[track_key] = (now, now)
            _gc_ergo_expired(store, duration_s, now)
            det["ergonomics"] = ergo_result
            det["ergonomics"]["sustained_duration_s"] = 0.0
            det["ergonomics"]["required_duration_s"] = duration_s
            det["ergonomics"]["is_arming"] = True
            det["is_ergonomic"] = True
            return False

        started_at, _ = entry
        store[track_key] = (started_at, now)
        elapsed = now - started_at
        _gc_ergo_expired(store, duration_s, now)

        det["ergonomics"] = ergo_result
        det["ergonomics"]["sustained_duration_s"] = round(elapsed, 1)
        det["ergonomics"]["required_duration_s"] = duration_s
        det["is_ergonomic"] = True

        if elapsed >= duration_s:
            base_reason = ergo_result.get("risk_reason", "High ergonomic posture risk")
            det["risk_reason"] = (
                f"Sustained awkward posture for {elapsed:.1f}s (REBA: {ergo_result.get('reba_score')}, "
                f"threshold: {duration_s:.0f}s) — {base_reason}"
            )
            return True
        else:
            return False

