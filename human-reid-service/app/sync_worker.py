import os
import time
import logging
import threading
import requests
from datetime import datetime, timezone
from uuid import UUID
from sqlalchemy.orm import Session
from .database import SessionLocal
from .models import PersonEmbedding

logger = logging.getLogger(__name__)

# Configuration
CLOUD_REID_URL = os.getenv("CLOUD_REID_URL", "").rstrip("/")
DEVICE_TENANT_ID = os.getenv("DEVICE_TENANT_ID", "")
SYNC_INTERVAL_SECONDS = int(os.getenv("SYNC_INTERVAL_SECONDS", "300"))
STATE_FILE_PATH = os.getenv("SYNC_STATE_FILE_PATH", ".reid_last_sync")

_sync_thread = None
_stop_event = threading.Event()


def start_sync_worker():
    """Start the background sync worker thread if edge configuration is present."""
    global _sync_thread
    if not CLOUD_REID_URL or not DEVICE_TENANT_ID:
        logger.info(
            "Sync worker disabled: CLOUD_REID_URL or DEVICE_TENANT_ID not set. "
            "Assuming this instance is running in Cloud mode or as a standalone local-only host."
        )
        return

    try:
        UUID(DEVICE_TENANT_ID)
    except ValueError:
        logger.error(f"SYNC WORKER ERROR: DEVICE_TENANT_ID is not a valid UUID: {DEVICE_TENANT_ID!r}")
        return

    logger.info(
        f"Starting Edge Re-ID Sync Worker. Target Cloud Re-ID: {CLOUD_REID_URL}, "
        f"Tenant ID: {DEVICE_TENANT_ID}, Interval: {SYNC_INTERVAL_SECONDS}s"
    )
    _stop_event.clear()
    _sync_thread = threading.Thread(target=_sync_loop, daemon=True, name="ReidSyncWorker")
    _sync_thread.start()


def stop_sync_worker():
    """Stop the background sync worker thread."""
    global _sync_thread
    if _sync_thread:
        _stop_event.set()
        _sync_thread.join(timeout=5.0)
        _sync_thread = None
        logger.info("Edge Re-ID Sync Worker stopped.")


def _get_last_sync_time() -> str:
    """Read the last successful sync timestamp from local disk."""
    if os.path.exists(STATE_FILE_PATH):
        try:
            with open(STATE_FILE_PATH, "r", encoding="utf-8") as f:
                timestamp = f.read().strip()
                # Simple validation of format (ISO 8601)
                datetime.fromisoformat(timestamp)
                return timestamp
        except Exception as e:
            logger.warning(f"Failed to read sync state file {STATE_FILE_PATH}: {e}. Syncing from beginning.")
    return ""


def _save_last_sync_time(timestamp: str):
    """Write the last successful sync timestamp to local disk."""
    try:
        with open(STATE_FILE_PATH, "w", encoding="utf-8") as f:
            f.write(timestamp)
    except Exception as e:
        logger.warning(f"Failed to write sync state file {STATE_FILE_PATH}: {e}")


def _sync_loop():
    while not _stop_event.is_set():
        try:
            _perform_sync()
        except Exception as e:
            logger.exception(f"Unexpected error in sync execution: {e}")
        
        # Sleep incrementally checking the stop event
        for _ in range(SYNC_INTERVAL_SECONDS):
            if _stop_event.is_set():
                break
            time.sleep(1.0)


def _perform_sync():
    last_sync = _get_last_sync_time()
    params = {"tenant_id": DEVICE_TENANT_ID}
    if last_sync:
        params["since"] = last_sync

    url = f"{CLOUD_REID_URL}/embeddings/sync"
    logger.debug(f"Syncing from Cloud Re-ID URL: {url} (since={last_sync or 'beginning'})")
    
    try:
        response = requests.get(url, params=params, timeout=30.0)
        if response.status_code == 404:
            # Maybe old API version or endpoint not found, do nothing
            logger.warning(f"Sync endpoint not found (HTTP 404) on cloud Re-ID service at {url}")
            return
        response.raise_for_status()
        embeddings = response.json()
    except Exception as e:
        logger.error(f"Failed to fetch embeddings from cloud Re-ID service: {e}")
        return

    if not embeddings:
        logger.debug("No new embeddings to sync.")
        return

    logger.info(f"Received {len(embeddings)} new/updated embeddings from cloud. Syncing locally...")
    
    # 1. Update in-memory vector index and local disk snapshot directly (Zero-DB Edge support)
    max_created_at = last_sync
    try:
        from .in_memory_store import embedding_store
        for item in embeddings:
            created_at_str = item.get("created_at") or ""
            if not max_created_at or created_at_str > max_created_at:
                max_created_at = created_at_str

            embedding_store.add_embedding(
                tenant_id=UUID(item["tenant_id"]),
                person_id=item.get("person_id"),
                embedding=item["embedding"],
                id_val=UUID(item["id"]) if item.get("id") else None,
                frame_url=item.get("frame_url"),
            )
        local_cache_path = os.getenv("EMBEDDING_CACHE_FILE", ".reid_embedding_cache.json")
        embedding_store.save_to_disk(local_cache_path)
        logger.info("Updated in-memory embedding store with %d synced records", len(embeddings))
    except Exception as exc:
        logger.warning("Failed to update in-memory store during sync: %s", exc)

    # 2. Persist to local PostgreSQL if available
    db: Optional[Session] = None
    try:
        db = SessionLocal()
        new_records = 0
        updated_records = 0

        for item in embeddings:
            item_id = UUID(item["id"])
            created_at_str = item.get("created_at") or datetime.now(timezone.utc).isoformat()

            # Check if this embedding already exists locally
            existing = db.query(PersonEmbedding).filter(PersonEmbedding.id == item_id).first()
            if existing:
                existing.person_id = item.get("person_id")
                existing.camera_id = item.get("camera_id")
                existing.frame_url = item.get("frame_url")
                existing.metadata_json = item.get("metadata_json") or {}
                existing.embedding = item["embedding"]
                updated_records += 1
            else:
                db_embedding = PersonEmbedding(
                    id=item_id,
                    tenant_id=UUID(item["tenant_id"]),
                    embedding=item["embedding"],
                    person_id=item.get("person_id"),
                    camera_id=item.get("camera_id"),
                    frame_url=item.get("frame_url"),
                    metadata_json=item.get("metadata_json") or {},
                    created_at=datetime.fromisoformat(created_at_str.replace("Z", "+00:00")).replace(tzinfo=None)
                )
                db.add(db_embedding)
                new_records += 1

        db.commit()
        logger.info(f"Local DB sync complete. Added {new_records} new records, updated {updated_records} records.")
    except Exception as e:
        if db:
            db.rollback()
        logger.debug(f"Local DB sync skipped or unavailable (running in in-memory mode): {e}")
    finally:
        if db:
            db.close()

    if max_created_at:
        _save_last_sync_time(max_created_at)
