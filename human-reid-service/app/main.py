from fastapi import FastAPI, Depends, HTTPException
from sqlalchemy.orm import Session
from sqlalchemy import select
from typing import List
from . import models, schemas, database
from pgvector.sqlalchemy import Vector

app = FastAPI(title="Alpha Surveillance - Human Re-ID Service")

# Initialize database on startup
@app.on_event("startup")
def startup_event():
    database.init_db()

@app.get("/health")
def health_check():
    return {"status": "healthy"}

@app.post("/embeddings", response_model=schemas.EmbeddingResponse)
def create_embedding(request: schemas.EmbeddingCreate, db: Session = Depends(database.get_db)):
    db_embedding = models.PersonEmbedding(
        tenant_id=request.tenant_id,
        embedding=request.embedding,
        person_id=request.person_id,
        camera_id=request.camera_id,
        frame_url=request.frame_url,
        metadata_json=request.metadata_json
    )
    db.add(db_embedding)
    db.commit()
    db.refresh(db_embedding)
    return db_embedding

@app.post("/search", response_model=List[schemas.SearchResult])
def search_person(request: schemas.SearchRequest, db: Session = Depends(database.get_db)):
    # pgvector cosine distance: embedding <=> target_vector
    # Similarity = 1 - Cosine Distance
    
    # query = select(
    #     models.PersonEmbedding,
    #     (1 - models.PersonEmbedding.embedding.cosine_distance(request.embedding)).label("similarity")
    # ).filter(models.PersonEmbedding.tenant_id == request.tenant_id)
    
    # For simplicity in this demo, we'll use a direct query approach
    # The <=> operator is cosine distance
    
    items = db.query(
        models.PersonEmbedding,
        (1 - models.PersonEmbedding.embedding.cosine_distance(request.embedding)).label("score")
    ).filter(
        models.PersonEmbedding.tenant_id == request.tenant_id
    ).order_by(
        models.PersonEmbedding.embedding.cosine_distance(request.embedding)
    ).limit(request.top_k).all()
    
    results = []
    for item, score in items:
        if score >= request.threshold:
            results.append(schemas.SearchResult(
                id=item.id,
                person_id=item.person_id,
                score=float(score),
                frame_url=item.frame_url,
                created_at=item.created_at
            ))
            
    return results
