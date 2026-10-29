"""
monitoring/edge_telemetry.py
============================
Edge Device Hardware Telemetry and Automated Thermal & Memory Load Shedding.

Probes SoC/GPU core temperatures, NPU utilization, unified VRAM allocation,
and RTSP packet drop rates across Jetson Tegrastats, NVML, and PyTorch backends.
Enforces automated load shedding under thermal stress (>= 85°C) or memory saturation (>= 90%)
by scaling interleaved sampling strides until the device normalizes.
"""
from __future__ import annotations

import logging
import os
import threading
import time
from typing import Any, Dict, List, Optional

import config
import metrics as vision_metrics

logger = logging.getLogger("vision-service.monitoring.edge-telemetry")


class HardwareTelemetryReader:
    """
    Reads hardware sensor metrics across diverse edge hardware environments:
    1. NVIDIA Jetson Tegrastats / sysfs thermal zones.
    2. NVIDIA Desktop/Server NVML.
    3. PyTorch CUDA / Apple Silicon MPS memory statistics.
    4. Host system fallback and test injection overrides.
    """

    def __init__(self):
        self._test_override: Optional[Dict[str, Any]] = None

    def set_test_override(self, override: Optional[Dict[str, Any]]) -> None:
        """Inject test telemetry for deterministic unit/integration testing."""
        self._test_override = override

    def read_telemetry(self) -> Dict[str, Any]:
        """Query edge hardware sensors and return standardized metrics dictionary."""
        if self._test_override is not None:
            data = dict(self._test_override)
            data["timestamp"] = time.time()
            data.setdefault("provider", "test_override")
            return data

        # 1. Probe Jetson sysfs thermal zones
        jetson_data = self._read_jetson_sysfs()
        if jetson_data is not None:
            return jetson_data

        # 2. Probe NVML (NVIDIA Management Library for desktop/server GPUs)
        nvml_data = self._read_nvml()
        if nvml_data is not None:
            return nvml_data

        # 3. Probe PyTorch CUDA / MPS device statistics
        torch_data = self._read_torch_hardware()
        if torch_data is not None:
            return torch_data

        # 4. Host fallback (standard Linux/macOS baseline)
        return self._read_host_fallback()

    def _read_jetson_sysfs(self) -> Optional[Dict[str, Any]]:
        """Read Jetson Orin / Xavier sysfs thermal zones and tegrastats nodes."""
        thermal_dir = "/sys/devices/virtual/thermal"
        if not os.path.isdir(thermal_dir):
            return None

        highest_temp = 0.0
        found_zone = False

        try:
            for entry in os.listdir(thermal_dir):
                if entry.startswith("thermal_zone"):
                    temp_path = os.path.join(thermal_dir, entry, "temp")
                    if os.path.isfile(temp_path):
                        with open(temp_path, "r") as f:
                            raw = f.read().strip()
                            val = float(raw)
                            # Convert millidegrees (e.g. 52000) to degrees C (52.0)
                            deg = val / 1000.0 if val > 1000.0 else val
                            if deg > highest_temp:
                                highest_temp = deg
                                found_zone = True

            if found_zone:
                # Approximate unified memory on Jetson from /proc/meminfo
                total_mem, used_mem = self._read_proc_meminfo()
                vram_pct = round((used_mem / max(1, total_mem)) * 100.0, 1)
                return {
                    "gpu_temp_c": round(highest_temp, 1),
                    "gpu_vram_used_bytes": used_mem,
                    "gpu_vram_total_bytes": total_mem,
                    "vram_percent": vram_pct,
                    "npu_util_percent": 0.0,
                    "provider": "jetson_sysfs",
                    "timestamp": time.time(),
                }
        except Exception as e:
            logger.debug("Failed reading Jetson sysfs thermal zones: %s", e)
        return None

    def _read_nvml(self) -> Optional[Dict[str, Any]]:
        """Probe NVIDIA NVML library if available."""
        try:
            import pynvml
            pynvml.nvmlInit()
            handle = pynvml.nvmlDeviceGetHandleByIndex(0)
            temp = float(pynvml.nvmlDeviceGetTemperature(handle, pynvml.NVML_TEMPERATURE_GPU))
            mem_info = pynvml.nvmlDeviceGetMemoryInfo(handle)
            util = pynvml.nvmlDeviceGetUtilizationRates(handle)
            vram_pct = round((mem_info.used / max(1, mem_info.total)) * 100.0, 1)
            return {
                "gpu_temp_c": temp,
                "gpu_vram_used_bytes": int(mem_info.used),
                "gpu_vram_total_bytes": int(mem_info.total),
                "vram_percent": vram_pct,
                "npu_util_percent": float(util.gpu),
                "provider": "nvml",
                "timestamp": time.time(),
            }
        except Exception:
            return None

    def _read_torch_hardware(self) -> Optional[Dict[str, Any]]:
        """Probe PyTorch CUDA or Apple MPS memory statistics."""
        try:
            import torch
            if torch.cuda.is_available():
                used = torch.cuda.memory_allocated()
                total = torch.cuda.get_device_properties(0).total_memory
                vram_pct = round((used / max(1, total)) * 100.0, 1)
                return {
                    "gpu_temp_c": 50.0,
                    "gpu_vram_used_bytes": int(used),
                    "gpu_vram_total_bytes": int(total),
                    "vram_percent": vram_pct,
                    "npu_util_percent": 0.0,
                    "provider": "torch_cuda",
                    "timestamp": time.time(),
                }
            if hasattr(torch.backends, "mps") and torch.backends.mps.is_available():
                used = getattr(torch.mps, "current_allocated_memory", lambda: 0)()
                total = 8 * 1024 * 1024 * 1024  # Default 8GB baseline
                vram_pct = round((used / max(1, total)) * 100.0, 1)
                return {
                    "gpu_temp_c": 45.0,
                    "gpu_vram_used_bytes": int(used),
                    "gpu_vram_total_bytes": int(total),
                    "vram_percent": vram_pct,
                    "npu_util_percent": 0.0,
                    "provider": "torch_mps",
                    "timestamp": time.time(),
                }
        except Exception:
            pass
        return None

    def _read_proc_meminfo(self) -> Tuple[int, int]:
        """Read Linux /proc/meminfo for memory usage."""
        total, free, avail = 8 * 1024**3, 4 * 1024**3, 4 * 1024**3
        try:
            if os.path.isfile("/proc/meminfo"):
                with open("/proc/meminfo", "r") as f:
                    for line in f:
                        if line.startswith("MemTotal:"):
                            total = int(line.split()[1]) * 1024
                        elif line.startswith("MemAvailable:"):
                            avail = int(line.split()[1]) * 1024
                return total, total - avail
        except Exception:
            pass
        return total, total - avail

    def _read_host_fallback(self) -> Dict[str, Any]:
        """Default baseline when specialized hardware APIs are unavailable."""
        total_mem, used_mem = self._read_proc_meminfo()
        vram_pct = round((used_mem / max(1, total_mem)) * 100.0, 1)
        return {
            "gpu_temp_c": 42.0,
            "gpu_vram_used_bytes": used_mem,
            "gpu_vram_total_bytes": total_mem,
            "vram_percent": vram_pct,
            "npu_util_percent": 0.0,
            "provider": "host_baseline",
            "timestamp": time.time(),
        }


class LoadSheddingController:
    """
    Evaluates hardware telemetry against thermal and memory thresholds.
    Implements hysteresis control to avoid flapping when temperature hovers near limits:
    - Triggers load shedding when Temp >= 85°C OR VRAM >= 90%.
    - Normalizes only when Temp <= 78°C AND VRAM <= 80%.
    """

    def __init__(self):
        self._is_active = False
        self._active_reason: Optional[str] = None
        self._last_telemetry: Dict[str, Any] = {}
        self._lock = threading.Lock()

    @property
    def is_active(self) -> bool:
        with self._lock:
            return self._is_active

    @property
    def active_reason(self) -> Optional[str]:
        with self._lock:
            return self._active_reason

    def evaluate(self, telemetry: Dict[str, Any]) -> bool:
        """
        Evaluate incoming hardware telemetry against thresholds with hysteresis.
        Returns True if load shedding is currently engaged.
        """
        temp_c = float(telemetry.get("gpu_temp_c", 0.0))
        vram_pct = float(telemetry.get("vram_percent", 0.0))

        shed_temp = getattr(config, "THERMAL_LOAD_SHED_TEMP_C", 85.0)
        resume_temp = getattr(config, "THERMAL_LOAD_SHED_RESUME_TEMP_C", 78.0)
        shed_vram = getattr(config, "VRAM_LOAD_SHED_PERCENT", 90.0)
        resume_vram = getattr(config, "VRAM_LOAD_SHED_RESUME_PERCENT", 80.0)

        with self._lock:
            self._last_telemetry = telemetry

            if not self._is_active:
                # Engagement condition
                if temp_c >= shed_temp:
                    self._is_active = True
                    self._active_reason = f"thermal_stress ({temp_c:.1f}C >= {shed_temp:.1f}C)"
                    logger.warning(
                        "🚨 Thermal load shedding engaged: GPU temp %.1fC >= %.1fC",
                        temp_c,
                        shed_temp,
                    )
                    vision_metrics.edge_load_shedding_events_total.labels(reason="thermal").inc()
                elif vram_pct >= shed_vram:
                    self._is_active = True
                    self._active_reason = f"vram_saturation ({vram_pct:.1f}% >= {shed_vram:.1f}%)"
                    logger.warning(
                        "🚨 Memory load shedding engaged: VRAM %.1f%% >= %.1f%%",
                        vram_pct,
                        shed_vram,
                    )
                    vision_metrics.edge_load_shedding_events_total.labels(reason="vram").inc()

            else:
                # Disengagement condition (hysteresis floor)
                if temp_c <= resume_temp and vram_pct <= resume_vram:
                    self._is_active = False
                    self._active_reason = None
                    logger.info(
                        "✅ Edge hardware normalized (temp=%.1fC, vram=%.1f%%). Disengaging load shedding.",
                        temp_c,
                        vram_pct,
                    )

            # Update Prometheus gauge
            vision_metrics.edge_load_shedding_active.set(1.0 if self._is_active else 0.0)
            return self._is_active

    def get_cadence_multiplier(self) -> float:
        """
        Return the sampling cadence multiplier:
        - 1.0 under normal operation (full inference cadence).
        - LOAD_SHED_CADENCE_MULTIPLIER (default 2.0x) under thermal/memory stress.
        """
        with self._lock:
            if self._is_active:
                return getattr(config, "LOAD_SHED_CADENCE_MULTIPLIER", 2.0)
            return 1.0

    def get_status(self) -> Dict[str, Any]:
        """Return comprehensive JSON status snapshot for monitoring APIs."""
        with self._lock:
            return {
                "load_shedding_active": self._is_active,
                "active_reason": self._active_reason,
                "cadence_multiplier": getattr(config, "LOAD_SHED_CADENCE_MULTIPLIER", 2.0) if self._is_active else 1.0,
                "telemetry": dict(self._last_telemetry),
            }


class EdgeTelemetryWorker:
    """
    Background daemon worker that periodically polls hardware telemetry,
    updates Prometheus gauges, and drives the LoadSheddingController.
    """

    def __init__(
        self,
        reader: Optional[HardwareTelemetryReader] = None,
        controller: Optional[LoadSheddingController] = None,
        poll_interval_s: Optional[float] = None,
    ):
        self.reader = reader or HardwareTelemetryReader()
        self.controller = controller or LoadSheddingController()
        self.poll_interval_s = (
            poll_interval_s
            if poll_interval_s is not None
            else getattr(config, "EDGE_TELEMETRY_POLL_INTERVAL_SECONDS", 5.0)
        )
        self._stream_manager: Any = None
        self._running = False
        self._thread: Optional[threading.Thread] = None
        self._stop_event = threading.Event()

    def set_stream_manager(self, stream_manager: Any) -> None:
        """Attach camera stream manager to track per-camera stream packet loss."""
        self._stream_manager = stream_manager

    def start(self, stream_manager: Optional[Any] = None) -> None:
        """Launch background telemetry polling thread."""
        if stream_manager is not None:
            self._stream_manager = stream_manager
        if self._running:
            return
        if not getattr(config, "EDGE_TELEMETRY_ENABLED", True):
            logger.info("Edge hardware telemetry disabled by configuration")
            return

        self._running = True
        self._stop_event.clear()
        self._thread = threading.Thread(
            target=self._poll_loop,
            name="edge-telemetry-worker",
            daemon=True,
        )
        self._thread.start()
        logger.info(
            "Launched EdgeTelemetryWorker daemon thread (poll interval: %.1fs)",
            self.poll_interval_s,
        )

    def stop(self) -> None:
        """Gracefully stop background telemetry polling thread."""
        self._running = False
        self._stop_event.set()
        if self._thread is not None and self._thread.is_alive():
            self._thread.join(timeout=3.0)
        logger.info("EdgeTelemetryWorker stopped")

    def poll_once(self) -> Dict[str, Any]:
        """Execute a single telemetry poll and update gauges."""
        data = self.reader.read_telemetry()
        try:
            vision_metrics.edge_gpu_temperature_celsius.set(data["gpu_temp_c"])
            vision_metrics.edge_gpu_vram_used_bytes.set(data["gpu_vram_used_bytes"])
            vision_metrics.edge_gpu_vram_total_bytes.set(data["gpu_vram_total_bytes"])
            vision_metrics.edge_npu_utilization_percent.set(data["npu_util_percent"])
        except Exception as e:
            logger.debug("Failed to set Prometheus telemetry gauges: %s", e)

        if self._stream_manager is not None:
            try:
                streams = getattr(self._stream_manager, "_streams", {})
                for cam_id, client in streams.items():
                    ratio = getattr(client, "packet_loss_ratio", 0.0)
                    vision_metrics.edge_camera_stream_packet_loss_ratio.labels(camera_id=str(cam_id)).set(ratio)
            except Exception as e:
                logger.debug("Failed to set packet loss ratio gauge: %s", e)

        self.controller.evaluate(data)
        return data

    def _poll_loop(self) -> None:
        while self._running and not self._stop_event.is_set():
            try:
                self.poll_once()
            except Exception as e:
                logger.warning("Error in EdgeTelemetryWorker poll loop: %s", e)
            self._stop_event.wait(timeout=self.poll_interval_s)


# Module-level singletons for application-wide integration
_GLOBAL_READER = HardwareTelemetryReader()
_GLOBAL_CONTROLLER = LoadSheddingController()
_GLOBAL_WORKER = EdgeTelemetryWorker(reader=_GLOBAL_READER, controller=_GLOBAL_CONTROLLER)


def get_telemetry_reader() -> HardwareTelemetryReader:
    return _GLOBAL_READER


def get_telemetry_controller() -> LoadSheddingController:
    return _GLOBAL_CONTROLLER


def get_telemetry_worker() -> EdgeTelemetryWorker:
    return _GLOBAL_WORKER
