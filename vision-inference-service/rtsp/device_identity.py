"""
rtsp/device_identity.py
───────────────────────
Stable per-host identifier for the Vision Inference Service so the
Violation Management API can hand back only the cameras assigned to this
specific edge device.

Resolution priority:
  1. DEVICE_ID env var                 — explicit override (k8s/EKS secrets,
                                          Docker compose overrides). Always wins.
  2. Contents of DEVICE_IDENTIFIER_FILE — a UUID written on first boot. Survives
                                          container restarts when the file path
                                          is mounted on a persistent volume.
  3. Newly-generated UUID4             — written to DEVICE_IDENTIFIER_FILE for
                                          future boots. Last resort.

We deliberately do NOT use MAC address as a primary source: container runtimes
randomise MAC on every restart, and NIC swaps on bare-metal hosts would silently
re-assign cameras to a "new" device.

The registration round-trip is performed during the FastAPI lifespan startup:

    identifier = get_device_identifier()
    device_id  = await register_device(api_client, identifier, hostname, tenant_id)

`device_id` is then passed into the ViolationApiClient on every
`fetch_active_cameras(device_id=...)` call. The API filters cameras to those
assigned to that device or to the shared pool (DeviceId IS NULL) for the
device's tenant.
"""
from __future__ import annotations

import logging
import os
import socket
import uuid
from pathlib import Path
from typing import Optional

import httpx

import config

logger = logging.getLogger(__name__)


def get_device_identifier() -> str:
    """
    Return a stable identifier for this host. See module docstring for the
    resolution order. Side-effect: may write a new UUID to
    ``config.DEVICE_IDENTIFIER_FILE`` on first boot.
    """
    # 1) explicit override
    explicit = (config.DEVICE_ID or "").strip()
    if explicit:
        logger.info("Device identifier from DEVICE_ID env var: %s", _short(explicit))
        return explicit

    # 2) persisted file
    path = Path(config.DEVICE_IDENTIFIER_FILE).expanduser()
    if path.exists():
        try:
            existing = path.read_text(encoding="utf-8").strip()
            if existing:
                logger.info("Device identifier from %s: %s", path, _short(existing))
                return existing
        except OSError as e:
            logger.warning("Could not read %s (%s) — will regenerate", path, e)

    # 3) generate new UUID, persist for future boots
    generated = str(uuid.uuid4())
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(generated, encoding="utf-8")
        try:
            os.chmod(path, 0o600)
        except OSError:
            pass  # not critical on platforms where chmod isn't meaningful
        logger.info("Generated new device identifier and persisted to %s: %s", path, _short(generated))
    except OSError as e:
        logger.warning(
            "Could not persist device identifier to %s (%s) — using in-memory only. "
            "The device may register as new on every restart.",
            path, e,
        )
    return generated


async def register_device(
    api_client,
    identifier: str,
    *,
    tenant_id: str,
    display_name: str = "",
    hostname: Optional[str] = None,
) -> Optional[str]:
    """
    POST /api/devices/internal/register against the Violation API. Returns the
    server-assigned device UUID on success, or None on failure (vision service
    then falls back to the legacy single-device flow).

    The endpoint is idempotent — calling twice with the same (TenantId,
    DeviceIdentifier) returns the same device row, refreshing LastSeenAt.
    """
    if not tenant_id:
        logger.warning(
            "DEVICE_TENANT_ID is not set — skipping device registration. "
            "The vision service will request ALL active cameras (legacy mode). "
            "Set DEVICE_TENANT_ID in your .env to enable per-device camera scoping."
        )
        return None

    resolved_hostname = hostname or socket.gethostname()
    payload = {
        "deviceIdentifier": identifier,
        "tenantId": tenant_id,
        "hostname": resolved_hostname,
        "displayName": display_name or resolved_hostname,
    }

    url = f"{api_client._base_url}/api/devices/internal/register"
    try:
        response = await api_client._http.post(url, json=payload, timeout=15.0)
        response.raise_for_status()
        data = response.json()
        device_id = data.get("deviceId")
        is_new = data.get("isNew", False)
        if not device_id:
            logger.error("Device register returned no deviceId: %s", data)
            return None
        logger.info(
            "Edge device %s as %s (identifier=%s, hostname=%s, tenant=%s)",
            "registered" if is_new else "re-attached",
            device_id, _short(identifier), resolved_hostname, _short(tenant_id),
        )
        return device_id
    except httpx.HTTPStatusError as e:
        logger.error(
            "Device registration failed: HTTP %d — %s",
            e.response.status_code, e.response.text[:200],
        )
        return None
    except (httpx.RequestError, httpx.TimeoutException) as e:
        logger.error("Device registration network error: %s", e)
        return None
    except Exception as e:  # noqa: BLE001
        logger.exception("Unexpected device registration error: %s", e)
        return None


def _short(value: str) -> str:
    """Mask the middle of an identifier in logs."""
    if not value or len(value) <= 12:
        return value
    return f"{value[:6]}…{value[-4:]}"
