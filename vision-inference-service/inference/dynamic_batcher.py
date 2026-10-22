"""
inference/dynamic_batcher.py
Central Dynamic Batch Scheduler (Triton / TorchServe pattern).
Coalesces concurrent inference requests across cameras into a single forward pass
within a tight timeout window (e.g. 5ms), maximizing GPU/NPU utilization and
completely eliminating cross-camera mutex lock contention.
"""
from __future__ import annotations

import logging
import queue
import threading
import time
from typing import Any, Callable, Dict, List, Optional

logger = logging.getLogger("vision-service.inference.dynamic_batcher")


class DynamicBatchScheduler:
    """
    Thread-safe dynamic batching scheduler.
    Receives individual or chunked inference items from multiple camera threads,
    accumulates them within a micro-wait window (default 5ms) or until max_batch_size
    is reached, and executes a single batched model forward pass.
    """

    def __init__(
        self,
        batch_predict_fn: Callable[[List[Any]], List[Any]],
        *,
        name: str = "batcher",
        max_batch_size: int = 8,
        max_wait_ms: float = 5.0,
    ):
        self.batch_predict_fn = batch_predict_fn
        self.name = name
        self.max_batch_size = max(1, int(max_batch_size))
        self.max_wait_seconds = max(0.0005, float(max_wait_ms) / 1000.0)

        self._queue: queue.Queue = queue.Queue()
        self._stop_event = threading.Event()
        self.stats: Dict[str, int] = {
            "total_batches": 0,
            "total_items": 0,
            "coalesced_batches": 0,
        }
        self._stats_lock = threading.Lock()

        self._worker_thread = threading.Thread(
            target=self._batch_loop,
            name=f"dynamic-batcher-{name}",
            daemon=True,
        )
        self._worker_thread.start()

    @property
    def is_running(self) -> bool:
        return not self._stop_event.is_set() and self._worker_thread.is_alive()

    def submit(self, item: Any, timeout: float = 10.0) -> Any:
        """
        Submit a single item (frame or crop) for batched inference.
        Blocks the calling camera thread until the batched forward pass completes.
        """
        if self._stop_event.is_set():
            raise RuntimeError(f"DynamicBatchScheduler '{self.name}' is stopped.")

        response_queue: queue.Queue = queue.Queue(maxsize=1)
        self._queue.put((item, response_queue))

        try:
            success, result = response_queue.get(timeout=timeout)
        except queue.Empty as err:
            raise TimeoutError(
                f"DynamicBatchScheduler '{self.name}' timed out after {timeout}s"
            ) from err

        if not success:
            raise result
        return result

    def submit_batch(self, items: List[Any], timeout: float = 10.0) -> List[Any]:
        """
        Submit a list of items (e.g. person crops from a single camera frame).
        Dispatches all items to the dynamic batcher and gathers their results.
        """
        if not items:
            return []
        if self._stop_event.is_set():
            raise RuntimeError(f"DynamicBatchScheduler '{self.name}' is stopped.")

        # Create one response queue per item
        response_queues = [queue.Queue(maxsize=1) for _ in items]
        for it, rq in zip(items, response_queues):
            self._queue.put((it, rq))

        results = []
        for rq in response_queues:
            try:
                success, res = rq.get(timeout=timeout)
            except queue.Empty as err:
                raise TimeoutError(
                    f"DynamicBatchScheduler '{self.name}' timed out after {timeout}s"
                ) from err
            if not success:
                raise res
            results.append(res)
        return results

    def _batch_loop(self):
        """
        Internal worker loop:
        1. Waits for first available item.
        2. Gathers additional items arriving within max_wait_seconds up to max_batch_size.
        3. Executes batch_predict_fn once for the coalesced batch.
        4. Distributes outputs to individual callers' response queues.
        """
        logger.debug("DynamicBatchScheduler '%s' loop started", self.name)
        while not self._stop_event.is_set():
            try:
                first_item, first_rq = self._queue.get(timeout=0.1)
            except queue.Empty:
                continue

            batch_items = [first_item]
            batch_rqs = [first_rq]
            start_time = time.monotonic()

            # Fast coalesce: gather up to max_batch_size items within max_wait_seconds
            while len(batch_items) < self.max_batch_size:
                elapsed = time.monotonic() - start_time
                remaining = self.max_wait_seconds - elapsed
                if remaining <= 0:
                    break
                try:
                    next_item, next_rq = self._queue.get(timeout=remaining)
                    batch_items.append(next_item)
                    batch_rqs.append(next_rq)
                except queue.Empty:
                    break

            # Execute the batched forward pass
            try:
                batch_results = self.batch_predict_fn(batch_items)
                if len(batch_results) != len(batch_items):
                    raise ValueError(
                        f"DynamicBatchScheduler '{self.name}' batch_predict_fn returned "
                        f"{len(batch_results)} outputs for {len(batch_items)} inputs"
                    )
                for rq, res in zip(batch_rqs, batch_results):
                    rq.put((True, res))

                with self._stats_lock:
                    self.stats["total_batches"] += 1
                    self.stats["total_items"] += len(batch_items)
                    if len(batch_items) > 1:
                        self.stats["coalesced_batches"] += 1

            except Exception as e:
                logger.error(
                    "DynamicBatchScheduler '%s' batch execution failed on %d items: %s",
                    self.name,
                    len(batch_items),
                    e,
                    exc_info=True,
                )
                for rq in batch_rqs:
                    rq.put((False, e))
            finally:
                for _ in batch_items:
                    self._queue.task_done()

        # Drain any lingering requests after stop_event
        while not self._queue.empty():
            try:
                _, rq = self._queue.get_nowait()
                rq.put((False, RuntimeError(f"DynamicBatchScheduler '{self.name}' was stopped.")))
                self._queue.task_done()
            except queue.Empty:
                break

        logger.debug("DynamicBatchScheduler '%s' loop exited", self.name)

    def stop(self, timeout: float = 2.0):
        """Signal worker thread to stop and wait for join."""
        self._stop_event.set()
        if self._worker_thread.is_alive():
            self._worker_thread.join(timeout=timeout)

        # Ensure all queued callers receive an error rather than hanging
        while not self._queue.empty():
            try:
                _, rq = self._queue.get_nowait()
                rq.put((False, RuntimeError(f"DynamicBatchScheduler '{self.name}' is stopped.")))
                self._queue.task_done()
            except queue.Empty:
                break
