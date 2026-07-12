"""
inference/preprocess.py

Low-light enhancement for CCTV frames before YOLO inference.

Uses CLAHE (Contrast Limited Adaptive Histogram Equalization) on the L channel
of LAB color space. This preserves color fidelity and only boosts local
luminance contrast — exactly what dim porch / night-kitchen scenes need.

A gamma lift is applied *only* on very dark frames (mean L < 90) so that
already-well-lit frames are not blown out.

Gated behind RESTAURANT_PPE_ENHANCE_LOWLIGHT (default: true). Set to "false"
to A/B compare against raw input.

Cost on 1920x1080: ~3-6 ms on CPU. Safe to run every frame.
"""
from __future__ import annotations

import logging

import cv2
import numpy as np
from PIL import Image

import config as _vis_config

logger = logging.getLogger("vision-service.inference.preprocess")

# M20 fix: CLAHE clip / tile-grid and gamma lift constants were hard-coded.
# Reading from config so deployments with different sensor noise floors can
# tune without code change. The CLAHE object is still created once per
# module load (threadsafe for .apply()), which means changing the env var
# requires a process restart — acceptable, those rarely change at runtime.
_CLAHE = cv2.createCLAHE(
    clipLimit=_vis_config.CLAHE_CLIP_LIMIT,
    tileGridSize=(_vis_config.CLAHE_TILE_GRID, _vis_config.CLAHE_TILE_GRID),
)

# Precomputed gamma LUT to avoid rebuilding it every frame.
_GAMMA = _vis_config.GAMMA_DARK_VALUE
_GAMMA_LUT = np.array(
    [((i / 255.0) ** (1.0 / _GAMMA)) * 255 for i in range(256)]
).astype("uint8")

# Threshold for triggering the gamma lift. If the mean luminance of the L
# channel is below this, the frame is considered dim.
_DARK_MEAN_THRESHOLD = _vis_config.GAMMA_DARK_THRESHOLD


def enhance_low_light(pil_image: Image.Image) -> Image.Image:
    """
    Return a CLAHE-enhanced copy of ``pil_image``.

    Always applies CLAHE; conditionally applies a gamma lift on dark frames.
    Input is expected to be RGB PIL; output is RGB PIL.
    """
    try:
        bgr = cv2.cvtColor(np.array(pil_image), cv2.COLOR_RGB2BGR)
        lab = cv2.cvtColor(bgr, cv2.COLOR_BGR2LAB)
        l, a, b = cv2.split(lab)
        l = _CLAHE.apply(l)
        if l.mean() < _DARK_MEAN_THRESHOLD:
            l = cv2.LUT(l, _GAMMA_LUT)
        merged = cv2.merge((l, a, b))
        rgb = cv2.cvtColor(cv2.cvtColor(merged, cv2.COLOR_LAB2BGR), cv2.COLOR_BGR2RGB)
        return Image.fromarray(rgb)
    except Exception:  # noqa: BLE001
        # Never let preprocessing kill inference; fall back to the original frame.
        logger.exception("enhance_low_light failed; using original frame.")
        return pil_image
