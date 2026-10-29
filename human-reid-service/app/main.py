import logging
import os
import threading
import time
from datetime import datetime, timezone
from contextlib import asynccontextmanager
from uuid import UUID

from fastapi import FastAPI, Depends, Header, HTTPException, Query, Request
from fastapi.responses import JSONResponse
from sqlalchemy import select
from sqlalchemy.exc import OperationalError, SQLAlchemyError
from sqlalchemy.orm import Session
from typing import Dict, List, Optional

from . import models, schemas, database
from .models import EMBEDDING_DIM, _utcnow
from .sync_worker import start_sync_worker, stop_sync_worker
from .in_memory_store import embedding_store


def verify_api_key(x_internal_api_key: Optional[str] = Header(None, alias="X-Internal-Api-Key")):
    """Verify service-to-service authentication if INTERNAL_API_KEY is configured."""
    expected_key = os.getenv("INTERNAL_API_KEY", "").strip()
    if not expected_key:
        return
    if not x_internal_api_key or x_internal_api_key != expected_key:
        raise HTTPException(status_code=401, detail="Invalid or missing X-Internal-Api-Key header")

import base64
import io
from PIL import Image
import numpy as np

# Load face_recognition library if available
try:
    import face_recognition
    HAS_FACE_RECOGNITION = True
except ImportError:
    HAS_FACE_RECOGNITION = False

FACE_MIN_DIM_PX = 45 # Minimum face dimensions for validation

logger = logging.getLogger(__name__)

if not HAS_FACE_RECOGNITION:
    logger.warning("face_recognition library not installed. Facial extraction endpoint is disabled.")


# R1 fix: how often (at most) a degraded service re-attempts DB initialization.
DB_RETRY_INTERVAL_SECONDS = float(os.getenv("DB_RETRY_INTERVAL_SECONDS", "15"))


# ---------------------------------------------------------------------------
# R1 fix: lazy DB recovery. Previously a failed init_db() at startup left
# db_ready False forever — /health returned 503 even after Postgres came back,
# so orchestrators kill-looped the pod. Now every /health (and every guarded
# endpoint) re-attempts init_db, rate-limited to once per
# DB_RETRY_INTERVAL_SECONDS.
# ---------------------------------------------------------------------------
def _attempt_db_recovery(app: FastAPI) -> bool:
    """Re-attempt database initialization if it previously failed.

    Rate-limited and guarded by a non-blocking lock so concurrent requests
    never pile up behind a recovery attempt. Returns the current readiness.
    """
    state = app.state
    if getattr(state, "db_ready", False):
        return True

    lock: threading.Lock = state.db_recovery_lock
    if not lock.acquire(blocking=False):
        return False  # another request is already probing
    try:
        now = time.monotonic()
        if now - state.last_db_attempt < DB_RETRY_INTERVAL_SECONDS:
            return False
        state.last_db_attempt = now
        try:
            # Single attempt here — the /health caller shouldn't block for the
            # full 5x3s retry ladder used at startup.
            database.init_db(max_retries=1, retry_delay_seconds=0)
            state.db_ready = True
            logger.info("Database recovered; leaving degraded mode.")
            start_sync_worker()
            return True
        except Exception as exc:  # noqa: BLE001
            logger.warning("Database still unavailable during recovery attempt: %s", exc)
            return False
    finally:
        lock.release()


def ensure_tenant_loaded(tenant_id: UUID, db: Optional[Session]) -> None:
    """Load vectors for a single tenant on demand if not already in RAM."""
    if embedding_store.is_tenant_loaded(tenant_id):
        return
    if db is None:
        return
    try:
        rows = db.query(models.PersonEmbedding).filter(models.PersonEmbedding.tenant_id == tenant_id).all()
        recs = [{
            "id": r.id,
            "person_id": r.person_id,
            "embedding": r.embedding if isinstance(r.embedding, list) else list(r.embedding),
            "frame_url": r.frame_url,
            "created_at": r.created_at,
        } for r in rows]
        embedding_store.load_tenant_records(tenant_id, recs)
        logger.info("Loaded %d vectors on demand for tenant %s", len(recs), tenant_id)
    except Exception as exc:
        logger.warning("Could not warm up tenant %s from DB: %s", tenant_id, exc)


def _warm_up_in_memory_store():
    """Warm up in-memory vector index for device tenant if configured, avoiding global DB dump."""
    device_tenant = os.getenv("DEVICE_TENANT_ID") or os.getenv("DEFAULT_TENANT_ID")
    if not device_tenant:
        logger.info("No DEVICE_TENANT_ID set; in-memory vector store will warm tenants on demand (lazy loading).")
        return
    try:
        tenant_uuid = UUID(device_tenant)
    except ValueError:
        logger.warning("Invalid DEVICE_TENANT_ID '%s'; skipping startup warmup.", device_tenant)
        return

    try:
        db = database.SessionLocal()
        try:
            ensure_tenant_loaded(tenant_uuid, db)
            logger.info("Warmed up device tenant %s vectors on startup", tenant_uuid)
        finally:
            db.close()
    except Exception as exc:
        logger.warning("Could not warm up device tenant %s on startup: %s", device_tenant, exc)


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Initialize database and load in-memory embedding cache."""
    app.state.db_ready = False
    app.state.db_recovery_lock = threading.Lock()
    app.state.last_db_attempt = time.monotonic()
    
    # 1. Load local persistent vector snapshot if present (zero-DB edge fast-start)
    local_cache_path = os.getenv("EMBEDDING_CACHE_FILE", ".reid_embedding_cache.json")
    if embedding_store.load_from_disk(local_cache_path):
        logger.info("Restored in-memory vector index from local snapshot: %s", local_cache_path)

    # 2. Initialize PostgreSQL if available
    try:
        database.init_db()
        app.state.db_ready = True
        _warm_up_in_memory_store()
        start_sync_worker()
    except Exception:
        logger.exception(
            "Database initialization failed during startup; service will start in "
            "degraded mode and re-attempt every %ss via /health.",
            DB_RETRY_INTERVAL_SECONDS,
        )
    yield
    stop_sync_worker()
    try:
        embedding_store.save_to_disk(local_cache_path)
    except Exception:
        pass


app = FastAPI(
    title="Alpha Surveillance – Human Re-ID Service",
    description=(
        "Vector-similarity search service for person re-identification. "
        f"Stores {EMBEDDING_DIM}-d face embeddings (dlib/face-api) per tenant "
        "and returns cosine-similarity matches."
    ),
    version="1.1.0",
    lifespan=lifespan,
)

# NOTE: endpoint authentication is intentionally out of scope for this fix
# round; the service must stay on a private network / behind the gateway.


# ---------------------------------------------------------------------------
# G4 fix: consistent error responses instead of raw 500 stack traces.
# ---------------------------------------------------------------------------
@app.exception_handler(OperationalError)
async def _handle_db_unavailable(request: Request, exc: OperationalError):
    logger.error("Database connection failure while handling %s: %s", request.url.path, exc)
    # Flip back to degraded so /health reflects reality and recovery kicks in.
    request.app.state.db_ready = False
    return JSONResponse(
        status_code=503,
        content={"detail": "Database unavailable; the service is in degraded mode. Retry shortly."},
    )


@app.exception_handler(SQLAlchemyError)
async def _handle_db_error(request: Request, exc: SQLAlchemyError):
    logger.exception("Database error while handling %s", request.url.path)
    return JSONResponse(
        status_code=500,
        content={"detail": "Internal database error."},
    )


def require_db(request: Request) -> None:
    """Guard for DB-backed endpoints: 503 with a clear message while degraded."""
    if not _attempt_db_recovery(request.app):
        raise HTTPException(
            status_code=503,
            detail="Database not ready; the service is in degraded mode. Retry shortly.",
        )


# ---------------------------------------------------------------------------
# Health
# ---------------------------------------------------------------------------
@app.get("/health", tags=["Ops"])
def health_check(request: Request):
    db_ok = _attempt_db_recovery(request.app)
    mem_size = embedding_store.size()
    if not db_ok and mem_size == 0:
        return JSONResponse(
            status_code=503,
            content={"status": "degraded", "service": "human-reid", "database": "unavailable", "in_memory_embeddings": 0},
        )
    return {
        "status": "healthy",
        "service": "human-reid",
        "database": "connected" if db_ok else "in_memory_only",
        "in_memory_embeddings": mem_size,
    }


# ---------------------------------------------------------------------------
# Embeddings — store a new embedding
# ---------------------------------------------------------------------------
@app.post(
    "/embeddings",
    response_model=schemas.EmbeddingResponse,
    tags=["ReID"],
    dependencies=[Depends(require_db), Depends(verify_api_key)],
)
def create_embedding(
    request: schemas.EmbeddingCreate,
    db: Session = Depends(database.get_db),
):
    db_embedding = models.PersonEmbedding(
        tenant_id=request.tenant_id,
        embedding=request.embedding,
        person_id=request.person_id,
        camera_id=request.camera_id,
        frame_url=request.frame_url,
        metadata_json=request.metadata_json or {},
    )
    db.add(db_embedding)
    db.commit()
    db.refresh(db_embedding)
    try:
        embedding_store.add_embedding(
            tenant_id=db_embedding.tenant_id,
            person_id=db_embedding.person_id,
            embedding=request.embedding,
            id_val=db_embedding.id,
            frame_url=db_embedding.frame_url,
            created_at=db_embedding.created_at,
        )
    except Exception as exc:
        logger.warning("Failed to update in-memory embedding store: %s", exc)
    return db_embedding


@app.post(
    "/embeddings/enroll-image",
    response_model=schemas.EmbeddingResponse,
    tags=["ReID"],
    dependencies=[Depends(require_db), Depends(verify_api_key)],
)
def enroll_from_image(
    request: schemas.EmbeddingImageCreate,
    db: Session = Depends(database.get_db),
):
    if not HAS_FACE_RECOGNITION:
        raise HTTPException(
            status_code=503,
            detail="face_recognition library is not loaded on this server instance.",
        )

    raw = request.image_base64 or ""
    if "," in raw and raw.strip().lower().startswith("data:"):
        raw = raw.split(",", 1)[1]

    try:
        image_bytes = base64.b64decode(raw, validate=True)
    except Exception:
        raise HTTPException(status_code=400, detail="image_base64 is not valid base64.")

    try:
        pil_image = Image.open(io.BytesIO(image_bytes)).convert("RGB")
    except Exception:
        raise HTTPException(status_code=400, detail="Could not decode image bytes as an image.")

    rgb = np.array(pil_image)
    rgb = np.ascontiguousarray(rgb, dtype=np.uint8)

    face_locations = face_recognition.face_locations(rgb, model="hog", number_of_times_to_upsample=1)
    if not face_locations:
        raise HTTPException(status_code=422, detail="No face detected in the image.")

    # Find the largest face
    largest_face_idx = 0
    max_area = 0
    for idx, (top, right, bottom, left) in enumerate(face_locations):
        area = (bottom - top) * (right - left)
        if area > max_area:
            max_area = area
            largest_face_idx = idx

    top, right, bottom, left = face_locations[largest_face_idx]
    face_h = bottom - top
    face_w = right - left
    if face_h < FACE_MIN_DIM_PX or face_w < FACE_MIN_DIM_PX:
        raise HTTPException(
            status_code=422,
            detail=f"Detected face too small ({face_w}x{face_h} px; min {FACE_MIN_DIM_PX} px).",
        )

    try:
        encodings = face_recognition.face_encodings(rgb, [face_locations[largest_face_idx]])
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"Model execution error: {exc}")

    if not encodings:
        raise HTTPException(status_code=422, detail="Could not extract vector representation from face.")

    embedding = encodings[0].tolist()

    db_embedding = models.PersonEmbedding(
        tenant_id=request.tenant_id,
        embedding=embedding,
        person_id=request.person_id,
        camera_id=request.camera_id,
        frame_url=request.frame_url,
        metadata_json=request.metadata_json or {},
    )
    db.add(db_embedding)
    db.commit()
    db.refresh(db_embedding)
    try:
        embedding_store.add_embedding(
            tenant_id=db_embedding.tenant_id,
            person_id=db_embedding.person_id,
            embedding=embedding,
            id_val=db_embedding.id,
            frame_url=db_embedding.frame_url,
            created_at=db_embedding.created_at,
        )
    except Exception as exc:
        logger.warning("Failed to update in-memory embedding store from image: %s", exc)
    return db_embedding


@app.get(
    "/embeddings/sync",
    tags=["ReID"],
    dependencies=[Depends(require_db), Depends(verify_api_key)],
)
def sync_embeddings(
    tenant_id: UUID,
    since: Optional[datetime] = Query(None, description="Only fetch embeddings created or deleted since this UTC timestamp"),
    db: Session = Depends(database.get_db),
):
    """
    Get all embeddings and deleted person tombstones for a tenant since a given timestamp.
    Used by Edge Sync Workers to incrementally download additions and evictions from Cloud DB.
    """
    emb_query = db.query(models.PersonEmbedding).filter(models.PersonEmbedding.tenant_id == tenant_id)
    tomb_query = db.query(models.PersonTombstone).filter(models.PersonTombstone.tenant_id == tenant_id)

    if since:
        # Strip timezone if present, keeping UTC naive parity
        since_naive = since.replace(tzinfo=None) if since.tzinfo else since
        emb_query = emb_query.filter(models.PersonEmbedding.created_at > since_naive)
        tomb_query = tomb_query.filter(models.PersonTombstone.deleted_at > since_naive)

    items = emb_query.order_by(models.PersonEmbedding.created_at.asc()).all()
    tombstones = tomb_query.order_by(models.PersonTombstone.deleted_at.asc()).all()
    now_iso = datetime.now(timezone.utc).isoformat()

    return {
        "items": [
            {
                "id": str(item.id),
                "tenant_id": str(item.tenant_id),
                "embedding": item.embedding if isinstance(item.embedding, list) else list(item.embedding),
                "person_id": item.person_id,
                "camera_id": item.camera_id,
                "frame_url": item.frame_url,
                "metadata_json": item.metadata_json or {},
                "created_at": item.created_at.isoformat() if item.created_at else now_iso,
            }
            for item in items
        ],
        "deleted_person_ids": list({t.person_id for t in tombstones}),
        "sync_timestamp": now_iso,
    }


# ---------------------------------------------------------------------------
# Search — find the nearest embeddings for a given query vector
# ---------------------------------------------------------------------------
def _build_search_statement(request: schemas.SearchRequest):
    """
    Optimized HNSW vector search.
    Orders directly by cosine_distance using pgvector's HNSW/IVFFlat index with an
    oversampled limit (top_k * 4, minimum 20).
    Per-person deduplication is performed in application logic to preserve
    index traversal efficiency and avoid full-table scans.
    """
    distance = models.PersonEmbedding.embedding.cosine_distance(request.embedding)
    fetch_limit = max(20, request.top_k * 4)
    return (
        select(
            models.PersonEmbedding.id,
            models.PersonEmbedding.person_id,
            (1 - distance).label("score"),
            models.PersonEmbedding.frame_url,
            models.PersonEmbedding.created_at,
        )
        .where(models.PersonEmbedding.tenant_id == request.tenant_id)
        .where(distance <= 1.0 - request.threshold)
        .order_by(distance)
        .limit(fetch_limit)
    )


def get_optional_db():
    """FastAPI dependency yielding a DB session if available, or None in zero-DB edge mode."""
    try:
        db = database.SessionLocal()
        try:
            yield db
        finally:
            db.close()
    except Exception:
        yield None


@app.post(
    "/search",
    response_model=List[schemas.SearchResult],
    tags=["ReID"],
)
def search_person(
    request: schemas.SearchRequest,
    req: Request,
    db: Optional[Session] = Depends(get_optional_db),
):
    """
    Sub-millisecond cosine-similarity search using pre-normalized in-memory NumPy matrix,
    with automatic database fallback.
    Returns at most one (best) match per person_id with score >= threshold,
    ordered by score descending, capped at top_k.
    """
    # Ensure tenant vectors are loaded in RAM (on-demand lazy loading)
    ensure_tenant_loaded(request.tenant_id, db)

    # ⚡ 1. Fast in-memory search (<0.2ms, zero DB disk I/O)
    if embedding_store.has_tenant(request.tenant_id):
        mem_results = embedding_store.search(
            tenant_id=request.tenant_id,
            query_embedding=request.embedding,
            threshold=request.threshold,
            top_k=request.top_k,
        )
        return [
            schemas.SearchResult(
                id=r["id"],
                person_id=r["person_id"],
                score=float(r["score"]),
                frame_url=r["frame_url"],
                created_at=r["created_at"],
            )
            for r in mem_results
        ]
    elif embedding_store.is_tenant_loaded(request.tenant_id):
        # Tenant was checked against DB and has 0 embeddings in store
        return []

    # 2. Cache miss: execute SQL query against database if available (e.g. DB recovery)
    if db is not None and _attempt_db_recovery(req.app):
        rows = db.execute(_build_search_statement(request)).mappings().all()

        # Deduplicate per person_id keeping highest similarity score
        best_per_person: Dict[str, dict] = {}
        for row in rows:
            sc = float(row["score"])
            pid = row["person_id"]
            key = pid if pid is not None else f"anon_{row['id']}"
            if key not in best_per_person or sc > best_per_person[key]["score"]:
                best_per_person[key] = {
                    "id": row["id"],
                    "person_id": pid,
                    "score": sc,
                    "frame_url": row["frame_url"],
                    "created_at": row["created_at"],
                }

        sorted_matches = sorted(best_per_person.values(), key=lambda x: x["score"], reverse=True)[:request.top_k]
        results = [schemas.SearchResult(**m) for m in sorted_matches]

        # Warm up in-memory index for this tenant so all subsequent searches run at RAM speed
        ensure_tenant_loaded(request.tenant_id, db)
        return results

    # If neither in-memory vectors nor DB are available for this tenant
    raise HTTPException(
        status_code=503,
        detail="Database not ready and in-memory embeddings not loaded for tenant.",
    )


# ---------------------------------------------------------------------------
# Delete — remove all stored embeddings for a specific person in a tenant
# (G1 fix: the duplicate /embeddings/{tenant_id}/{person_id} route was removed;
#  this is the route the Violation Management API's FaceScanController calls.)
# ---------------------------------------------------------------------------
@app.delete(
    "/embeddings/person/{person_id}",
    tags=["ReID"],
    dependencies=[Depends(require_db), Depends(verify_api_key)],
)
def delete_person_embeddings(
    person_id: str,
    tenant_id: str = Query(..., description="Tenant UUID"),
    db: Session = Depends(database.get_db),
):
    """
    Delete every stored embedding for *person_id* within *tenant_id*.
    Call this before re-enrolling a person so stale vectors don't pollute searches.
    """
    try:
        tenant_uuid = UUID(tenant_id)
    except (ValueError, AttributeError, TypeError):
        raise HTTPException(status_code=400, detail=f"tenant_id is not a valid UUID: {tenant_id!r}")

    deleted = (
        db.query(models.PersonEmbedding)
        .filter(
            models.PersonEmbedding.tenant_id == tenant_uuid,
            models.PersonEmbedding.person_id == person_id,
        )
        .delete(synchronize_session=False)
    )
    # Record tombstone so edge sync workers evict this person
    try:
        tombstone = models.PersonTombstone(
            tenant_id=tenant_uuid,
            person_id=person_id,
            deleted_at=_utcnow(),
        )
        db.add(tombstone)
    except Exception as tomb_err:
        logger.warning("Failed to record deletion tombstone for %s: %s", person_id, tomb_err)
    db.commit()

    # Also evict from in-memory vector index
    try:
        embedding_store.delete_person(tenant_uuid, person_id)
    except Exception as exc:
        logger.debug("In-memory person deletion failed (non-fatal): %s", exc)
    return {"deleted": deleted, "person_id": person_id}
