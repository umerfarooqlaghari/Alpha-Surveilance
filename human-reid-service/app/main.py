import logging
import os
import threading
import time
from datetime import datetime
from contextlib import asynccontextmanager
from uuid import UUID

from fastapi import FastAPI, Depends, HTTPException, Query, Request
from fastapi.responses import JSONResponse
from sqlalchemy import select
from sqlalchemy.exc import OperationalError, SQLAlchemyError
from sqlalchemy.orm import Session
from typing import List, Optional

from . import models, schemas, database
from .models import EMBEDDING_DIM
from .sync_worker import start_sync_worker, stop_sync_worker

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


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Initialize the database if possible and mark startup as degraded otherwise."""
    app.state.db_ready = False
    app.state.db_recovery_lock = threading.Lock()
    app.state.last_db_attempt = time.monotonic()
    try:
        database.init_db()
        app.state.db_ready = True
        start_sync_worker()
    except Exception:
        logger.exception(
            "Database initialization failed during startup; service will start in "
            "degraded mode and re-attempt every %ss via /health.",
            DB_RETRY_INTERVAL_SECONDS,
        )
    yield
    stop_sync_worker()


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
    if not _attempt_db_recovery(request.app):
        return JSONResponse(
            status_code=503,
            content={"status": "degraded", "service": "human-reid", "database": "unavailable"},
        )
    return {"status": "healthy", "service": "human-reid"}


# ---------------------------------------------------------------------------
# Embeddings — store a new embedding
# ---------------------------------------------------------------------------
@app.post(
    "/embeddings",
    response_model=schemas.EmbeddingResponse,
    tags=["ReID"],
    dependencies=[Depends(require_db)],
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
    return db_embedding


@app.post(
    "/embeddings/enroll-image",
    response_model=schemas.EmbeddingResponse,
    tags=["ReID"],
    dependencies=[Depends(require_db)],
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
    return db_embedding


@app.get(
    "/embeddings/sync",
    response_model=List[schemas.EmbeddingResponse],
    tags=["ReID"],
    dependencies=[Depends(require_db)],
)
def sync_embeddings(
    tenant_id: UUID,
    since: Optional[datetime] = Query(None, description="Only fetch embeddings created since this UTC timestamp"),
    db: Session = Depends(database.get_db),
):
    """
    Get all embeddings for a tenant, optionally filtered since a given timestamp.
    Used by the Edge Sync Worker to incrementally download embeddings from the Cloud DB.
    """
    query = db.query(models.PersonEmbedding).filter(models.PersonEmbedding.tenant_id == tenant_id)
    if since:
        # Strip timezone if present, keeping UTC naive parity
        since_naive = since.replace(tzinfo=None) if since.tzinfo else since
        query = query.filter(models.PersonEmbedding.created_at > since_naive)
    
    return query.order_by(models.PersonEmbedding.created_at.asc()).all()


# ---------------------------------------------------------------------------
# Search — find the nearest embeddings for a given query vector
# ---------------------------------------------------------------------------
def _build_search_statement(request: schemas.SearchRequest):
    """R5/G2 fix: push threshold + per-person dedup into SQL.

    Inner query: DISTINCT ON (person_id) keeps only the best (closest) row
    per person, with the similarity threshold applied as
    cosine_distance <= 1 - threshold so the ANN index can be used.
    Outer query: order the per-person best rows by score and apply top_k.
    Dedup happens BEFORE the limit, so the caller's known-match margin logic
    always sees a true second-best candidate from a DIFFERENT person.
    """
    distance = models.PersonEmbedding.embedding.cosine_distance(request.embedding)
    best_per_person = (
        select(
            models.PersonEmbedding.id,
            models.PersonEmbedding.person_id,
            (1 - distance).label("score"),
            models.PersonEmbedding.frame_url,
            models.PersonEmbedding.created_at,
        )
        .where(models.PersonEmbedding.tenant_id == request.tenant_id)
        .where(distance <= 1.0 - request.threshold)
        .distinct(models.PersonEmbedding.person_id)
        .order_by(models.PersonEmbedding.person_id, distance)
        .subquery("best_per_person")
    )
    return (
        select(best_per_person)
        .order_by(best_per_person.c.score.desc())
        .limit(request.top_k)
    )


@app.post(
    "/search",
    response_model=List[schemas.SearchResult],
    tags=["ReID"],
    dependencies=[Depends(require_db)],
)
def search_person(
    request: schemas.SearchRequest,
    db: Session = Depends(database.get_db),
):
    """
    Cosine-similarity search using pgvector's <=> operator.
    Returns at most one (best) match per person_id with score >= threshold,
    ordered by score descending, capped at top_k.
    """
    rows = db.execute(_build_search_statement(request)).mappings().all()
    return [
        schemas.SearchResult(
            id=row["id"],
            person_id=row["person_id"],
            score=float(row["score"]),
            frame_url=row["frame_url"],
            created_at=row["created_at"],
        )
        for row in rows
    ]


# ---------------------------------------------------------------------------
# Delete — remove all stored embeddings for a specific person in a tenant
# (G1 fix: the duplicate /embeddings/{tenant_id}/{person_id} route was removed;
#  this is the route the Violation Management API's FaceScanController calls.)
# ---------------------------------------------------------------------------
@app.delete(
    "/embeddings/person/{person_id}",
    tags=["ReID"],
    dependencies=[Depends(require_db)],
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
    db.commit()
    return {"deleted": deleted, "person_id": person_id}
