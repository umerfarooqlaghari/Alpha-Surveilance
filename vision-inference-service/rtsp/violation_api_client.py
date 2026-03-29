"""
rtsp/violation_api_client.py
HTTP client for fetching camera configurations from the Violation Management API.
Uses httpx with automatic retries. Decoupled from the stream engine — can be used
independently or mocked in tests.
"""
import logging
import httpx
import json
from typing import List, Optional

from .models import CameraConfig, ViolationRule

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
    ):
        if not base_url:
            raise ValueError("VIOLATION_API_BASE_URL must be set")
        
        self._base_url = base_url.rstrip("/")
        self._api_key = api_key or ""
        self._timeout = timeout_seconds
        self._max_retries = max_retries

    async def fetch_active_cameras(self) -> List[CameraConfig]:
        """
        Calls GET /api/cameras/internal/active and returns a list of CameraConfig.
        Retries on transient failures with a simple linear delay.
        Returns empty list on permanent failure (caller decides what to do).
        """
        url = f"{self._base_url}/api/cameras/internal/active"
        headers = {"X-Internal-Api-Key": self._api_key}

        for attempt in range(1, self._max_retries + 1):
            try:
                async with httpx.AsyncClient(timeout=self._timeout) as client:
                    response = await client.get(url, headers=headers)
                    response.raise_for_status()

                    data = response.json()
                    cameras = self._parse_cameras(data)
                    logger.info(
                        "Fetched %d active cameras from Violation API (attempt %d)",
                        len(cameras), attempt
                    )
                    return cameras

            except httpx.HTTPStatusError as e:
                logger.error(
                    "Violation API returned HTTP %d for internal cameras (attempt %d/%d): %s",
                    e.response.status_code, attempt, self._max_retries, e
                )
                if e.response.status_code in (401, 403):
                    # Auth errors won't fix themselves — fail fast
                    logger.critical("Internal API key rejected! Check INTERNAL_API_KEY config.")
                    raise

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
                import asyncio
                await asyncio.sleep(attempt * 2)  # linear backoff: 2s, 4s

        logger.error("All %d attempts to fetch cameras failed. No streams will start.", self._max_retries)
        return []

    def _parse_cameras(self, data: list) -> List[CameraConfig]:
        cameras = []
        for item in data:
            try:
                rules = []
                for r in item.get("violationRules", []):
                    labels_str = r.get("triggerLabels", "")
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

                    rules.append(ViolationRule(
                        sop_violation_type_id=str(r["sopViolationTypeId"]),
                        model_identifier=model_id_str,
                        trigger_labels=labels
                    ))

                config = CameraConfig(
                    camera_db_id=str(item["id"]),
                    camera_id=str(item["cameraId"]),
                    tenant_id=str(item["tenantId"]),
                    tenant_name=str(item.get("tenantName", "Unknown Tenant")),
                    rtsp_url=str(item["rtspUrl"]),
                    whip_url=str(item.get("whipUrl", "")),
                    is_streaming=bool(item.get("isStreaming", False)),
                    name=str(item.get("name", "")),
                    location=str(item.get("location", "")),
                    violation_rules=rules,
                )
                cameras.append(config)
            except (KeyError, TypeError) as e:
                logger.warning("Skipping malformed camera entry %s: %s", item, e)
        return cameras

    async def post_violation(self, payload: dict) -> bool:
        """
        POST a single violation directly to POST /api/violations/internal.
        """
        url = f"{self._base_url}/api/violations/internal"
        headers = {
            "X-Internal-Api-Key": self._api_key,
            "Content-Type": "application/json",
        }
        try:
            async with httpx.AsyncClient(timeout=5.0) as client:
                # Payload is wrapped in a list for bulk compatibility
                response = await client.post(url, json=[payload], headers=headers)
                if response.status_code not in (200, 201):
                    logger.error("API rejected violation: HTTP %d - %s", response.status_code, response.text)
                    return False
                return True
        except Exception as e:
            logger.error("Failed to POST violation to API: %s", e)
            return False

    async def get_active_violation(self, camera_db_id: str, track_id: int) -> Optional[dict]:
        """
        GET /api/violations/internal/active?cameraId={camera_db_id}&trackId={track_id}
        Checks if there's already an 'Active' record in the DB for this track.
        """
        url = f"{self._base_url}/api/violations/internal/active"
        params = {"cameraId": camera_db_id, "trackId": track_id}
        headers = {"X-Internal-Api-Key": self._api_key}
        try:
            async with httpx.AsyncClient(timeout=5.0) as client:
                response = await client.get(url, params=params, headers=headers)
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
        headers = {"X-Internal-Api-Key": self._api_key, "Content-Type": "application/json"}
        payload = {"Timestamp": timestamp}
        try:
            async with httpx.AsyncClient(timeout=5.0) as client:
                response = await client.patch(url, json=payload, headers=headers)
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
        headers = {"X-Internal-Api-Key": self._api_key}
        try:
            async with httpx.AsyncClient(timeout=5.0) as client:
                response = await client.get(url, headers=headers)
                if response.status_code == 200:
                    return response.json()
                return None
        except Exception as e:
            logger.error("Failed to fetch violation settings: %s", e)
            return None

