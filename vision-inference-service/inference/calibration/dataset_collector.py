"""
inference/calibration/dataset_collector.py
=========================================
Representative dataset collector for INT8 TensorRT entropy calibration.

Collects and pre-processes representative surveillance frames across diverse
edge environments (kitchens, warehouses, cash counters, night/day shifts)
to build the dynamic range histogram required for IInt8EntropyCalibrator2.
"""
from __future__ import annotations

import logging
import os
from typing import Generator, List, Optional, Tuple, Union

import numpy as np
from PIL import Image

logger = logging.getLogger("vision-service.inference.calibration.dataset")


class CalibrationDatasetCollector:
    """
    Collects, normalizes, and batches representative surveillance images
    for INT8 TensorRT dynamic range histogram calibration.
    """

    SUPPORTED_SCENES = ("kitchen", "warehouse", "cash_counter", "outdoor_dock")

    def __init__(
        self,
        image_size: Tuple[int, int] = (640, 640),
        batch_size: int = 8,
        num_frames: int = 500,
        source_dirs: Optional[List[str]] = None,
    ):
        self.width, self.height = image_size
        self.batch_size = max(1, batch_size)
        self.num_frames = max(self.batch_size, num_frames)
        self.source_dirs = source_dirs or []
        self._frames: List[np.ndarray] = []
        self._current_batch = 0

    def load_from_directories(self) -> int:
        """Scan configured directories for surveillance images."""
        loaded = 0
        extensions = (".jpg", ".jpeg", ".png", ".bmp")
        for sdir in self.source_dirs:
            if not os.path.isdir(sdir):
                continue
            for root, _, files in os.walk(sdir):
                for f in files:
                    if f.lower().endswith(extensions):
                        full_path = os.path.join(root, f)
                        try:
                            with Image.open(full_path) as img:
                                arr = self._preprocess_image(img)
                                self._frames.append(arr)
                                loaded += 1
                                if loaded >= self.num_frames:
                                    logger.info(
                                        "Loaded target %d frames from source directories",
                                        loaded,
                                    )
                                    return loaded
                        except Exception as e:
                            logger.debug("Failed to read calibration image %s: %s", full_path, e)
        logger.info("Loaded %d calibration frames from local storage", loaded)
        return loaded

    def synthesize_representative_surveillance_scenes(self, count: int) -> int:
        """
        Generate synthetic surveillance frames modeling diverse operational conditions:
        - Kitchen: warm tint, specular reflections on stainless steel, lower contrast
        - Warehouse: cool shadows, high dynamic range high-bay lighting
        - Cash counter: bright overhead spot lighting with localized glare
        - Outdoor dock: variable daylight and shadow gradients
        """
        rng = np.random.RandomState(42)
        generated = 0

        for i in range(count):
            scene_type = self.SUPPORTED_SCENES[i % len(self.SUPPORTED_SCENES)]
            frame = np.zeros((self.height, self.width, 3), dtype=np.float32)

            if scene_type == "kitchen":
                # Warm industrial ambient (yellowish cast, localized hot spots)
                base_color = np.array([0.45, 0.42, 0.35], dtype=np.float32)
                noise = rng.normal(0.0, 0.05, frame.shape).astype(np.float32)
                frame[:] = np.clip(base_color + noise, 0.0, 1.0)
                # Add stainless steel specular glare streak
                low_gx = max(2, int(self.width * 0.1))
                high_gx = max(low_gx + 2, int(self.width * 0.8))
                glare_x = rng.randint(low_gx, high_gx)
                glare_w = max(4, int(self.width * 0.1))
                frame[:, glare_x : min(self.width, glare_x + glare_w), :] = np.clip(
                    frame[:, glare_x : min(self.width, glare_x + glare_w), :] + 0.35, 0.0, 1.0
                )

            elif scene_type == "warehouse":
                # Dim background with periodic overhead light cones
                frame[:] = rng.uniform(0.10, 0.25, frame.shape).astype(np.float32)
                # Overhead bay lamps (bright circular highlights)
                low_cx, high_cx = max(2, int(self.width * 0.1)), max(4, int(self.width * 0.9))
                low_cy, high_cy = max(2, int(self.height * 0.1)), max(4, int(self.height * 0.6))
                radius = max(5, int(min(self.width, self.height) * 0.15))
                for _ in range(4):
                    cx = rng.randint(low_cx, high_cx)
                    cy = rng.randint(low_cy, high_cy)
                    y_idx, x_idx = np.ogrid[: self.height, : self.width]
                    mask = (x_idx - cx) ** 2 + (y_idx - cy) ** 2 <= radius**2
                    frame[mask] = np.clip(frame[mask] + 0.50, 0.0, 1.0)

            elif scene_type == "cash_counter":
                # High-contrast cash drawer point lighting
                frame[:] = 0.20
                y1, y2 = int(self.height * 0.25), int(self.height * 0.75)
                x1, x2 = int(self.width * 0.2), int(self.width * 0.8)
                frame[y1:y2, x1:x2] = 0.65  # counter surface
                noise = rng.normal(0.0, 0.03, frame.shape).astype(np.float32)
                frame = np.clip(frame + noise, 0.0, 1.0)

            else:  # outdoor_dock
                # High dynamic range daylight gradient
                grad = np.linspace(0.8, 0.2, self.height, dtype=np.float32)[:, None, None]
                frame[:] = grad
                shadow = rng.uniform(0.0, 0.04, frame.shape).astype(np.float32)
                frame = np.clip(frame + shadow, 0.0, 1.0)

            # Convert HWC (0..1) -> CHW (1, C, H, W) float32
            chw = np.transpose(frame, (2, 0, 1))
            self._frames.append(chw)
            generated += 1

        logger.info(
            "Synthesized %d representative surveillance frames across %s scenes",
            generated,
            ", ".join(self.SUPPORTED_SCENES),
        )
        return generated

    def prepare_dataset(self) -> int:
        """
        Ensure calibration pool is fully populated up to target num_frames.
        Uses local directory images first; fills remaining needed count via scene synthesizer.
        """
        loaded = self.load_from_directories()
        needed = self.num_frames - loaded
        if needed > 0:
            self.synthesize_representative_surveillance_scenes(needed)
        return len(self._frames)

    def _preprocess_image(self, img: Image.Image) -> np.ndarray:
        """Resize to target dimensions and format as normalized CHW float32 tensor."""
        if img.mode != "RGB":
            img = img.convert("RGB")
        img_resized = img.resize((self.width, self.height), Image.Resampling.BILINEAR)
        arr = np.asarray(img_resized, dtype=np.float32) / 255.0
        return np.transpose(arr, (2, 0, 1))

    @property
    def total_frames(self) -> int:
        return len(self._frames)

    @property
    def num_batches(self) -> int:
        return len(self._frames) // self.batch_size

    def get_batch(self, batch_idx: int) -> Optional[np.ndarray]:
        """
        Return a batch of frames shaped (batch_size, 3, height, width) as contiguous float32 array.
        """
        if self.total_frames < self.batch_size:
            self.prepare_dataset()

        start = batch_idx * self.batch_size
        end = start + self.batch_size
        if end > len(self._frames):
            return None

        batch_list = self._frames[start:end]
        batch_array = np.stack(batch_list, axis=0)
        return np.ascontiguousarray(batch_array, dtype=np.float32)

    def iter_batches(self) -> Generator[np.ndarray, None, None]:
        """Generator yielding all calibration batches sequentially."""
        if not self._frames:
            self.prepare_dataset()

        for b in range(self.num_batches):
            batch = self.get_batch(b)
            if batch is not None:
                yield batch
