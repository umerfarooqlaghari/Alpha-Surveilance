from pydantic import BaseModel
from typing import List, Optional, Dict, Any
from uuid import UUID
from datetime import datetime

class EmbeddingBase(BaseModel):
    tenant_id: UUID
    embedding: List[float]
    person_id: Optional[str] = None
    camera_id: Optional[str] = None
    frame_url: Optional[str] = None
    metadata_json: Optional[Dict[str, Any]] = {}

class EmbeddingCreate(EmbeddingBase):
    pass

class EmbeddingResponse(EmbeddingBase):
    id: UUID
    created_at: datetime

    class Config:
        from_attributes = True

class SearchRequest(BaseModel):
    tenant_id: UUID
    embedding: List[float]
    top_k: int = 5
    threshold: float = 0.5 # Cosine similarity threshold

class SearchResult(BaseModel):
    id: UUID
    person_id: Optional[str]
    score: float
    frame_url: Optional[str]
    created_at: datetime
