"""
inference/calibration/calibrator.py
==================================
Surveillance entropy calibrator for TensorRT INT8 optimization.

Implements TensorRT IInt8EntropyCalibrator2 protocol to compute optimal
scale factors (quantization dynamic ranges) for activation tensors using
Kullback-Leibler (KL) divergence minimization.
"""
from __future__ import annotations

import logging
import os
from typing import Any, List, Optional

import numpy as np

from inference.calibration.dataset_collector import CalibrationDatasetCollector

logger = logging.getLogger("vision-service.inference.calibration.calibrator")

# Attempt importing TensorRT; provide graceful base class fallback if unavailable
try:
    import tensorrt as trt
    _BaseCalibrator = trt.IInt8EntropyCalibrator2
    HAS_TRT = True
except (ImportError, AttributeError):
    class _BaseCalibrator:  # type: ignore[no-redef]
        pass
    HAS_TRT = False

# Attempt importing pycuda / torch for device memory allocations
try:
    import pycuda.autoinit  # noqa: F401
    import pycuda.driver as cuda
    HAS_PYCUDA = True
except (ImportError, Exception):
    HAS_PYCUDA = False


class SurveillanceEntropyCalibrator(_BaseCalibrator):
    """
    TensorRT IInt8EntropyCalibrator2 implementation for surveillance vision pipelines.

    Feeds representative batches of surveillance frames into TensorRT during
    calibration to compute activation quantization scales and write calibration cache.
    """

    def __init__(
        self,
        dataset: CalibrationDatasetCollector,
        cache_file: str,
        input_name: str = "images",
    ):
        if HAS_TRT and issubclass(_BaseCalibrator, trt.IInt8EntropyCalibrator2):
            super().__init__()

        self.dataset = dataset
        self.cache_file = cache_file
        self.input_name = input_name
        self.current_batch_idx = 0
        self.device_allocation = None

        os.makedirs(os.path.dirname(os.path.abspath(self.cache_file)), exist_ok=True)

        # Allocate device memory if CUDA/pycuda is available
        if HAS_PYCUDA:
            try:
                batch_bytes = (
                    self.dataset.batch_size
                    * 3
                    * self.dataset.height
                    * self.dataset.width
                    * 4  # float32 = 4 bytes
                )
                self.device_allocation = cuda.mem_alloc(batch_bytes)
                logger.info(
                    "Allocated %d bytes on GPU device for INT8 calibration batches",
                    batch_bytes,
                )
            except Exception as e:
                logger.warning("Failed to allocate CUDA memory for calibrator: %s", e)
                self.device_allocation = None

    def get_batch_size(self) -> int:
        """Return the batch size required by the calibration engine."""
        return self.dataset.batch_size

    def get_batch(self, names: Optional[List[str]] = None) -> Optional[List[int]]:
        """
        Return a list of device memory addresses containing the next calibration batch.
        Returns None when all batches have been consumed.
        """
        if self.dataset.total_frames < self.dataset.batch_size:
            self.dataset.prepare_dataset()

        if self.current_batch_idx >= self.dataset.num_batches:
            logger.info(
                "Completed all %d calibration batches (%d frames total)",
                self.dataset.num_batches,
                self.dataset.total_frames,
            )
            return None

        batch = self.dataset.get_batch(self.current_batch_idx)
        if batch is None:
            return None

        self.current_batch_idx += 1

        if HAS_PYCUDA and self.device_allocation is not None:
            # Transfer host contiguous numpy array to device pointer
            cuda.memcpy_htod(self.device_allocation, batch)
            return [int(self.device_allocation)]

        # On non-CUDA development hosts or stubs, return memory address
        return [batch.__array_interface__["data"][0]]

    def read_calibration_cache(self) -> Optional[bytes]:
        """Read pre-existing calibration cache from disk if available."""
        if os.path.isfile(self.cache_file):
            try:
                with open(self.cache_file, "rb") as f:
                    data = f.read()
                    logger.info(
                        "Loaded existing INT8 calibration cache from %s (%d bytes)",
                        self.cache_file,
                        len(data),
                    )
                    return data
            except Exception as e:
                logger.warning("Could not read calibration cache %s: %s", self.cache_file, e)
        return None

    def write_calibration_cache(self, cache: bytes) -> None:
        """Write freshly computed INT8 calibration cache to disk."""
        try:
            with open(self.cache_file, "wb") as f:
                f.write(cache)
            logger.info("Successfully wrote INT8 calibration cache to %s", self.cache_file)
        except Exception as e:
            logger.error("Failed to write INT8 calibration cache to %s: %s", self.cache_file, e)

    def reset(self) -> None:
        """Reset iteration back to the first batch."""
        self.current_batch_idx = 0
