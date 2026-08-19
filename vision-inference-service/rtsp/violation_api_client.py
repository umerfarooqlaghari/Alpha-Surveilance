"""
rtsp/violation_api_client.py
HTTP client for fetching camera configurations from the Violation Management API.
Uses httpx with automatic retries. Decoupled from the stream engine — can be used
independently or mocked in tests.
"""
import asyncio
import logging
import httpx
import json
import os
import random
import sqlite3
import threading
from collections import deque
from typing import Deque, List, Optional, Tuple

from .models import CameraConfig, ViolationRule, DetectionScheduleItem

logger = logging.getLogger(__name__)


class ViolationApiClient:
    """
    Fetches active camera configurations from the Violation Management API's
    internal endpoint. Sends X-Internal-Api-Key for service-to-service auth.
    """

    def __init__(
        self,
        base_url: str,
        api_key: str,
        timeout_seconds: float = 15.0,
        max_retries: int = 3,
        dlq_persist_path: Optional[str] = None,
    ):
        if not base_url:
            raise ValueError("VIOLATION_API_BASE_URL must be set")
        
        self._base_url = base_url.rstrip("/")
        self._api_key = api_key or ""
        self._timeout = timeout_seconds
        self._max_retries = max_retries

        # P2 fix #4: one pooled HTTP client for the lifetime of the service
        # instead of opening a new TCP+TLS connection per call. The default
        # request timeout is 5s (matching the old per-call settings); the
        # heavier 15s timeout is applied per-request on `fetch_active_cameras`.
        # Keepalive limits sized for ~10 cameras posting at ≤5 evt/s each.
        self._http = httpx.AsyncClient(
            timeout=httpx.Timeout(5.0),
            limits=httpx.Limits(
                max_keepalive_connections=20,
                max_connections=40,
                keepalive_expiry=30.0,
            ),
            headers={
                "X-Internal-Api-Key": self._api_key,
                "User-Agent": "alpha-vision-inference/1.0",
            },
        )

        # Audit P3 #11: dead-letter queue for `post_violation` retries.
        # Fire-and-forget callbacks lose violations whenever the API is
        # restarting (rolling deploy, OOM). We hold failed payloads in a
        # bounded in-memory deque and a background task retries them with
        # exponential backoff + jitter. The deque is capped at 10k so a
        # multi-day outage can't OOM the service — oldest entries are dropped
        # with a CRITICAL log when the cap is reached.
        #
        # H10 fix: deque entries are now ``(row_id, payload)`` tuples so we
        # can delete the matching SQLite row on successful drain. ``row_id``
        # is None when persistence is disabled.
        self._dlq: Deque[Tuple[Optional[int], dict]] = deque(maxlen=10_000)
        self._dlq_task: Optional[asyncio.Task] = None
        self._dlq_stopping = False
        self._dlq_retry_interval = 30.0  # seconds between drain attempts

        # H10 fix: optional SQLite-backed persistence so a service restart
        # (rolling deploy, OOM kill, host reboot) does not lose queued
        # violations. The DB is small (rows are JSON blobs of a few KB) and
        # access is serialised through a single connection + lock, which is
        # plenty for the DLQ's low write rate.
        self._dlq_db_path: Optional[str] = (dlq_persist_path or "").strip() or None
        self._dlq_db: Optional[sqlite3.Connection] = None
        self._dlq_db_lock = threading.Lock()
        if self._dlq_db_path:
            try:
                # check_same_thread=False so we can serialise via our own
                # lock from either the asyncio loop or worker threads.
                # isolation_level=None puts sqlite into autocommit, which
                # keeps the writes durable without manual commits.
                self._dlq_db = sqlite3.connect(
                    self._dlq_db_path,
                    check_same_thread=False,
                    isolation_level=None,
                )
                self._dlq_db.execute(
                    "CREATE TABLE IF NOT EXISTS dlq ("
                    "  id INTEGER PRIMARY KEY AUTOINCREMENT, "
                    "  payload TEXT NOT NULL, "
                    "  created REAL NOT NULL"
                    ")"
                )
                # Re-hydrate any rows that survived the previous shutdown.
                rows = self._dlq_db.execute(
                    "SELECT id, payload FROM dlq ORDER BY id ASC LIMIT ?",
                    (self._dlq.maxlen,),
                ).fetchall()
                loaded = 0
                for row_id, payload_json in rows:
                    try:
                        self._dlq.append((row_id, json.loads(payload_json)))
                        loaded += 1
                    except Exception:  # noqa: BLE001
                        # Corrupt row — drop and continue. We log once below.
                        try:
                            self._dlq_db.execute("DELETE FROM dlq WHERE id = ?", (row_id,))
                        except Exception:  # noqa: BLE001
                            pass
                if loaded:
                    logger.info(
                        "DLQ re-hydrated %d persisted violation(s) from %s",
                        loaded, self._dlq_db_path,
                    )
            except Exception as e:  # noqa: BLE001
                logger.error(
                    "DLQ persistence init failed at %s (memory-only mode): %s",
                    self._dlq_db_path, e,
                )
                self._dlq_db = None

    def start_background_workers(self) -> None:
        """Kick off the DLQ drain task. Call once from FastAPI lifespan after
        the event loop is running. Safe to call multiple times."""
        if self._dlq_task is None or self._dlq_task.done():
            self._dlq_stopping = False
            self._dlq_task = asyncio.create_task(self._drain_dlq_loop())

    async def aclose(self) -> None:
        """Release the pooled HTTP client. Idempotent; safe to call twice."""
        self._dlq_stopping = True
        if self._dlq_task is not None and not self._dlq_task.done():
            self._dlq_task.cancel()
            try:
                await self._dlq_task
            except (asyncio.CancelledError, Exception):  # noqa: BLE001
                pass
        try:
            await self._http.aclose()
        except Exception:  # noqa: BLE001
            pass
        # H10 fix: close the SQLite handle so the file is unlocked for the
        # next process. Unflushed entries remain on disk for re-hydration.
        if self._dlq_db is not None:
            with self._dlq_db_lock:
                try:
                    self._dlq_db.close()
                except Exception:  # noqa: BLE001
                    pass
                self._dlq_db = None

    async def fetch_active_cameras(self, device_id: Optional[str] = None) -> Optional[List[CameraConfig]]:
        """
        Calls GET /api/cameras/internal/active and returns a list of CameraConfig.
        Retries on transient failures with jittered exponential backoff.

        V5 fix — return contract:
          * ``list``  — the API answered 200. May legitimately be empty
                        (tenant really has zero active cameras).
          * ``None``  — the fetch FAILED (network error, timeout, 5xx, 401/403,
                        parse error after all retries). Callers MUST NOT treat
                        this as "zero cameras": reconciling against an empty
                        list on an API blip used to tear down every healthy
                        stream. Skip reconcile and keep existing streams.

        When ``device_id`` is supplied the API filters the response to cameras
        assigned to that device PLUS cameras in the device's tenant that have
        no device assignment (shared pool). When omitted, the legacy
        "all active cameras" response is returned — used for dev/single-device
        deployments.
        """
        url = f"{self._base_url}/api/cameras/internal/active"
        params = {"deviceId": device_id} if device_id else None

        for attempt in range(1, self._max_retries + 1):
            try:
                response = await self._http.get(url, params=params, timeout=self._timeout)
                response.raise_for_status()

                data = response.json()
                cameras = self._parse_cameras(data)
                logger.info(
                    "Fetched %d active cameras from Violation API (attempt %d, device=%s)",
                    len(cameras), attempt, device_id or "(all)"
                )
                return cameras

            except httpx.HTTPStatusError as e:
                logger.error(
                    "Violation API returned HTTP %d for internal cameras (attempt %d/%d): %s",
                    e.response.status_code, attempt, self._max_retries, e
                )
                if e.response.status_code in (401, 403):
                    # H12 fix: previously this re-raised, which crashed the
                    # `/streams/reload` and `/analyze` request handlers (the
                    # config-poll loop swallowed it but the direct callers
                    # did not). Auth errors are operational — log critically
                    # and return a failure sentinel so callers can render a
                    # degraded-but-alive response instead of 500.
                    # V5 fix: return None (fetch FAILED), not [] — an [] here
                    # made the poll loop reconcile to zero cameras and tear
                    # down every healthy stream on a key rotation mishap.
                    logger.critical(
                        "Internal API key rejected (HTTP %d). Treating camera fetch as FAILED "
                        "(existing streams are kept). Check INTERNAL_API_KEY and the backend's "
                        "expected secret.",
                        e.response.status_code,
                    )
                    return None

            except (httpx.ConnectError, httpx.TimeoutException) as e:
                logger.warning(
                    "Cannot reach Violation API (attempt %d/%d): %s",
                    attempt, self._max_retries, e
                )

            except Exception as e:
                logger.error(
                    "Unexpected error fetching cameras (attempt %d/%d): %s",
                    attempt, self._max_retries, e
                )

            if attempt < self._max_retries:
                # M18 fix: was linear `attempt * 2` (2s, 4s, …). Switched to
                # jittered exponential capped at 60s so a thundering herd of
                # edge devices reconnecting after a backend outage does not
                # synchronise. Cap at 60s because the outer poll interval is
                # typically 300s+ and longer waits would skip the poll cycle.
                import random as _r
                base = min(60.0, 2.0 ** attempt)
                jitter = _r.uniform(0.0, 0.5 * base)
                await asyncio.sleep(base + jitter)

        # V5 fix: all retries exhausted — this is a FAILURE, not "no cameras".
        logger.error(
            "All %d attempts to fetch cameras failed. Returning None so callers "
            "keep their existing streams instead of reconciling to zero.",
            self._max_retries,
        )
        return None

    def _parse_cameras(self, data: list) -> List[CameraConfig]:
        cameras = []
        for item in data:
            try:
                rules = []
                for r in item.get("violationRules", []):
                    labels_str = r.get("triggerLabels") or ""
                    try:
                        if labels_str.startswith("["):
                            import json
                            parsed = json.loads(labels_str)
                            labels = [str(l).strip().lower() for l in parsed if str(l).strip()]
                        else:
                            labels = [l.strip().lower() for l in labels_str.split(",") if l.strip()] if labels_str else []
                    except Exception:
                        labels = [l.strip().lower() for l in labels_str.split(",") if l.strip()] if labels_str else []
                    
                    model_id_str = str(r["modelIdentifier"])
                    if not labels and model_id_str in ("human-detection-v1", "hustvl/yolos-tiny"):
                        labels = ["person"]

                    # Parse Policy Configuration JSON. We treat any parse failure or
                    # non-object payload as "no policy" — the server validator should
                    # have rejected these on write, so this is just defense in depth.
                    rule_config: dict = {}
                    config_str = r.get("ruleConfigurationJson")
                    if config_str:
                        try:
                            parsed_cfg = json.loads(config_str)
                            if isinstance(parsed_cfg, dict):
                                rule_config = parsed_cfg
                            else:
                                logger.warning(
                                    "ruleConfigurationJson for rule %s is not a JSON object (got %s); ignoring.",
                                    r.get("sopViolationTypeId"), type(parsed_cfg).__name__,
                                )
                        except Exception as e:
                            logger.warning("Failed to parse ruleConfigurationJson: %s", e)

                    rules.append(ViolationRule(
                        sop_violation_type_id=str(r["sopViolationTypeId"]),
                        model_identifier=model_id_str,
                        trigger_labels=labels,
                        rule_config=rule_config,
                        # AiModel registry fields
                        model_status=str(r.get("modelStatus", "Available")),
                        model_type=str(r.get("modelType", "YoloLocal")),
                        model_download_url=r.get("modelDownloadUrl"),
                        model_s3_bucket=r.get("modelS3Bucket"),
                        model_s3_key=r.get("modelS3Key"),
                        model_min_confidence=float(r["modelMinConfidence"]) if r.get("modelMinConfidence") is not None else None,
                        model_image_size=int(r["modelImageSize"]) if r.get("modelImageSize") is not None else None,
                        model_local_path=r.get("modelLocalPath"),
                        model_sha256=r.get("modelSha256"),
                        ai_model_id=str(r["aiModelId"]) if r.get("aiModelId") else None,
                    ))

                config = CameraConfig(
                    camera_db_id=str(item["id"]),
                    camera_id=str(item["cameraId"]),
                    tenant_id=str(item["tenantId"]),
                    tenant_name=str(item.get("tenantName", "Unknown Tenant")),
                    rtsp_url=str(item["rtspUrl"]),
                    whip_url=str(item.get("whipUrl", "")),
                    is_streaming=bool(item.get("isStreaming", False)),
                    is_detection_enabled=item.get("isDetectionEnabled") is not False,
                    detection_schedules=[
                        DetectionScheduleItem(
                            start_time=str(s.get("startTime", "00:00")),
                            end_time=str(s.get("endTime", "00:00")),
                            days_of_week=int(s.get("daysOfWeek", 127)),
                            is_active=bool(s.get("isActive", True)),
                            label=str(s.get("label", "")),
                        )
                        for s in item.get("detectionSchedules", [])
                        if isinstance(s, dict)
                    ],
                    name=str(item.get("name", "")),
                    location=str(item.get("location", "")),
                    violation_rules=rules,
                    attendance_mode=str(item.get("attendanceMode", item.get("AttendanceMode", "None"))),
                )

                # Per-camera FPS override from API (falls back to model default of 1.0)
                try:
                    raw_fps = item.get("targetFps")
                    if raw_fps is not None:
                        fps_val = float(raw_fps)
                        if fps_val > 0:
                            config.target_fps = fps_val
                except (TypeError, ValueError):
                    logger.warning(
                        "Invalid targetFps value '%s' for camera %s — using default",
                        item.get("targetFps"), item.get("cameraId")
                    )

                cameras.append(config)
            except (KeyError, TypeError) as e:
                logger.warning("Skipping malformed camera entry %s: %s", item, e)
        return cameras

    async def post_violation(self, payload: dict) -> bool:
        """
        POST a single violation to /api/Violations/internal.

        On transient network/5xx failures the payload is pushed to the in-memory
        DLQ for background retry (see ``_drain_dlq_loop``). Returns True on
        immediate success, False on permanent reject (4xx) AND when the call
        was queued for retry — callers should treat False as "not yet stored".
        """
        ok, transient = await self._try_post_violation(payload)
        # Audit P4 #17: classify outcome for /metrics. We want to know not
        # just success vs failure but whether the failures are retryable.
        try:
            import metrics as _vm
            if ok:
                _vm.api_post_total.labels(outcome="success").inc()
            elif transient:
                _vm.api_post_total.labels(outcome="transient_fail").inc()
            else:
                _vm.api_post_total.labels(outcome="permanent_fail").inc()
        except Exception:  # noqa: BLE001
            pass
        if not ok and transient:
            self._enqueue_dlq(payload)
        return ok

    async def _try_post_violation(self, payload: dict) -> tuple:
        """Return (success, is_transient_failure).

        is_transient_failure=True means the caller should retry later (network
        error, 5xx, timeout). False means either success or a permanent
        rejection (4xx) where retrying would just re-fail.
        """
        url = f"{self._base_url}/api/Violations/internal"
        try:
            response = await self._http.post(
                url, json=[payload],
                headers={"Content-Type": "application/json"},
            )
            if response.status_code in (200, 201):
                return True, False
            if 400 <= response.status_code < 500:
                logger.error(
                    "API permanently rejected violation: HTTP %d - %s",
                    response.status_code, response.text,
                )
                return False, False
            logger.warning(
                "API returned transient %d for violation; queuing for retry: %s",
                response.status_code, response.text,
            )
            return False, True
        except (httpx.ConnectError, httpx.TimeoutException, httpx.NetworkError) as e:
            logger.warning("Network error posting violation (will retry): %s", e)
            return False, True
        except Exception as e:  # noqa: BLE001
            logger.error("Unexpected error posting violation: %s", e)
            return False, True

    def _enqueue_dlq(self, payload: dict) -> None:
        """Append to the DLQ. If the deque is at maxlen, the oldest entry is
        evicted automatically (collections.deque semantics). Log loudly so
        operators notice sustained back-pressure."""
        was_full = len(self._dlq) >= self._dlq.maxlen
        # H10 fix: write through to SQLite first so a crash between the
        # in-memory append and disk write doesn't lose the violation.
        row_id: Optional[int] = None
        if self._dlq_db is not None:
            try:
                with self._dlq_db_lock:
                    cur = self._dlq_db.execute(
                        "INSERT INTO dlq (payload, created) VALUES (?, ?)",
                        (json.dumps(payload, default=str), __import__("time").time()),
                    )
                    row_id = cur.lastrowid
                # Mirror deque eviction: if we just hit the cap, also evict
                # the corresponding oldest row from SQLite.
                if was_full and self._dlq:
                    oldest_row_id, _ = self._dlq[0]
                    if oldest_row_id is not None:
                        try:
                            with self._dlq_db_lock:
                                self._dlq_db.execute(
                                    "DELETE FROM dlq WHERE id = ?", (oldest_row_id,),
                                )
                        except Exception:  # noqa: BLE001
                            pass
            except Exception as e:  # noqa: BLE001
                logger.error("DLQ SQLite write failed (continuing in memory only): %s", e)
                row_id = None
        self._dlq.append((row_id, payload))
        if was_full:
            logger.critical(
                "Violation DLQ saturated (cap=%d) — oldest violation dropped. "
                "Violation API may be down for an extended period.",
                self._dlq.maxlen,
            )

    @property
    def dlq_size(self) -> int:
        return len(self._dlq)

    async def _drain_dlq_loop(self) -> None:
        """Background task: every ``_dlq_retry_interval`` seconds, attempt to
        flush the DLQ. On transient failure of a payload we put it BACK on the
        right side (preserving order) and break out so we don't burn the API
        with a hot loop — next tick will retry."""
        # jittered initial delay so multiple instances don't sync up
        await asyncio.sleep(random.uniform(2.0, 5.0))
        while not self._dlq_stopping:
            try:
                if self._dlq:
                    initial = len(self._dlq)
                    flushed = 0
                    while self._dlq and not self._dlq_stopping:
                        row_id, payload = self._dlq.popleft()
                        ok, transient = await self._try_post_violation(payload)
                        if ok:
                            flushed += 1
                            # H10 fix: violation has now landed in the API
                            # — drop its persisted copy so a future crash
                            # doesn't replay it.
                            if row_id is not None and self._dlq_db is not None:
                                try:
                                    with self._dlq_db_lock:
                                        self._dlq_db.execute(
                                            "DELETE FROM dlq WHERE id = ?", (row_id,),
                                        )
                                except Exception:  # noqa: BLE001
                                    pass
                            continue
                        if transient:
                            # API still down — put it back at front and pause
                            self._dlq.appendleft((row_id, payload))
                            break
                        # permanent failure: drop the payload (already logged).
                        # Also drop the persisted row so we don't replay it.
                        if row_id is not None and self._dlq_db is not None:
                            try:
                                with self._dlq_db_lock:
                                    self._dlq_db.execute(
                                        "DELETE FROM dlq WHERE id = ?", (row_id,),
                                    )
                            except Exception:  # noqa: BLE001
                                pass
                    if flushed:
                        logger.info(
                            "DLQ drain flushed %d/%d violations (remaining=%d)",
                            flushed, initial, len(self._dlq),
                        )
            except Exception as e:  # noqa: BLE001
                logger.error("DLQ drain loop crashed: %s", e)
            # Sleep with jitter; respect cancellation promptly.
            try:
                await asyncio.sleep(self._dlq_retry_interval * (1 + random.uniform(-0.2, 0.2)))
            except asyncio.CancelledError:
                break

    async def get_active_violation(self, camera_db_id: str, track_id: int) -> Optional[dict]:
        """
        GET /api/violations/internal/active?cameraId={camera_db_id}&trackId={track_id}
        Checks if there's already an 'Active' record in the DB for this track.
        """
        url = f"{self._base_url}/api/violations/internal/active"
        params = {"cameraId": camera_db_id, "trackId": track_id}
        try:
            response = await self._http.get(url, params=params)
            if response.status_code == 200:
                return response.json()
            return None
        except Exception as e:
            logger.error("Failed to check active violation: %s", e)
            return None

    async def update_violation(self, violation_id: str, timestamp: str) -> bool:
        """
        PATCH /api/violations/internal/{violation_id}
        Updates the LastSeen/Timestamp of an existing violation.
        """
        url = f"{self._base_url}/api/violations/internal/{violation_id}"
        payload = {"Timestamp": timestamp}
        try:
            response = await self._http.patch(
                url, json=payload,
                headers={"Content-Type": "application/json"},
            )
            return response.status_code == 200
        except Exception as e:
            logger.error("Failed to update violation: %s", e)
            return False

    async def get_violation_settings(self, violation_type: str) -> Optional[dict]:
        """
        GET /api/violations/internal/settings/{violation_type}
        Retrieves cooldown/suppression duration from the DB.
        """
        url = f"{self._base_url}/api/violations/internal/settings/{violation_type}"
        try:
            response = await self._http.get(url)
            if response.status_code == 200:
                return response.json()
            return None
        except Exception as e:
            logger.error("Failed to fetch violation settings: %s", e)
            return None

    async def post_attendance_record(self, payload: dict) -> bool:
        """
        POST /api/attendance/internal/record
        Dispatches recognized employee detection event to backend FILO attendance service.
        """
        url = f"{self._base_url}/api/attendance/internal/record"
        try:
            headers = {
                "Content-Type": "application/json",
                "X-Internal-Api-Key": self._api_key,
            }
            response = await self._http.post(url, json=payload, headers=headers)
            return response.status_code == 200
        except Exception as e:
            logger.error("Failed to post attendance record: %s", e)
            return False


