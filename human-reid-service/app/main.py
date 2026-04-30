from contextlib import asynccontextmanager
from fastapi import FastAPI, Depends, HTTPException
from sqlalchemy.orm import Session
from typing import List
from . import models, schemas, database


# ---------------------------------------------------------------------------
# Lifespan — replaces the deprecated @app.on_event("startup")
# ---------------------------------------------------------------------------
@asynccontextmanager
async def lifespan(app: FastAPI):
    """Run DB initialisation once at startup, then yield control to the app."""
    database.init_db()
    yield
    # (add any shutdown logic here if needed)


app = FastAPI(
    title="Alpha Surveillance – Human Re-ID Service",
    description=(
        "Vector-similarity search service for person re-identification. "
        "Stores 512-d OSNet embeddings per tenant and returns cosine-similarity matches."
    ),
    version="1.0.0",
    lifespan=lifespan,
)


# ---------------------------------------------------------------------------
# Health
# ---------------------------------------------------------------------------
@app.get("/health", tags=["Ops"])
def health_check():
    return {"status": "healthy", "service": "human-reid"}


# ---------------------------------------------------------------------------
# Embeddings — store a new embedding
# ---------------------------------------------------------------------------
@app.post("/embeddings", response_model=schemas.EmbeddingResponse, tags=["ReID"])
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


# ---------------------------------------------------------------------------
# Search — find the nearest embeddings for a given query vector
# ---------------------------------------------------------------------------
@app.post("/search", response_model=List[schemas.SearchResult], tags=["ReID"])
def search_person(
    request: schemas.SearchRequest,
    db: Session = Depends(database.get_db),
):
    """
    Cosine-similarity search using pgvector's <=> operator.
    Returns only results whose similarity score >= threshold.
    """
    items = (
        db.query(
            models.PersonEmbedding,
            (
                1 - models.PersonEmbedding.embedding.cosine_distance(request.embedding)
            ).label("score"),
        )
        .filter(models.PersonEmbedding.tenant_id == request.tenant_id)
        .order_by(
            models.PersonEmbedding.embedding.cosine_distance(request.embedding)
        )
        .limit(request.top_k)
        .all()
    )

    results = []
    for item, score in items:
        if score >= request.threshold:
            results.append(
                schemas.SearchResult(
                    id=item.id,
                    person_id=item.person_id,
                    score=float(score),
                    frame_url=item.frame_url,
                    created_at=item.created_at,
                )
            )

    return results


# ---------------------------------------------------------------------------
# Delete — remove all embeddings for a person across a tenant
# ---------------------------------------------------------------------------
@app.delete("/embeddings/{tenant_id}/{person_id}", tags=["ReID"])
def delete_person_embeddings(
    tenant_id: str,
    person_id: str,
    db: Session = Depends(database.get_db),
):
    deleted = (
        db.query(models.PersonEmbedding)
        .filter(
            models.PersonEmbedding.tenant_id == tenant_id,
            models.PersonEmbedding.person_id == person_id,
        )
        .delete()
    )
    db.commit()
    return {"deleted": deleted, "person_id": person_id}
