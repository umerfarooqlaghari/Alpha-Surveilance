from pydantic import BaseModel, ConfigDict, Field, field_validator
from typing import List, Optional, Dict, Any
from uuid import UUID
from datetime import datetime

from .models import EMBEDDING_DIM


def _validate_embedding_length(value: List[float]) -> List[float]:
    """R4 fix: reject wrong-length vectors up front with a 422 instead of a
    cryptic pgvector error deep inside the request handler."""
    if len(value) != EMBEDDING_DIM:
        raise ValueError(
            f"embedding must have exactly {EMBEDDING_DIM} dimensions, got {len(value)}"
        )
    return value


class EmbeddingBase(BaseModel):
    tenant_id: UUID
    embedding: List[float]
    person_id: Optional[str] = None
    camera_id: Optional[str] = None
    frame_url: Optional[str] = None
    # G5 fix: use a factory instead of a shared mutable default.
    metadata_json: Optional[Dict[str, Any]] = Field(default_factory=dict)

    @field_validator("embedding")
    @classmethod
    def _check_embedding_length(cls, value: List[float]) -> List[float]:
        return _validate_embedding_length(value)


class EmbeddingCreate(EmbeddingBase):
    pass


class EmbeddingImageCreate(BaseModel):
    tenant_id: UUID
    image_base64: str
    person_id: Optional[str] = None
    camera_id: Optional[str] = None
    frame_url: Optional[str] = None
    metadata_json: Optional[Dict[str, Any]] = Field(default_factory=dict)


class EmbeddingResponse(EmbeddingBase):
    id: UUID
    created_at: datetime

    model_config = ConfigDict(from_attributes=True)

    @field_validator("embedding", mode="before")
    @classmethod
    def _coerce_vector(cls, value):
        # pgvector returns numpy arrays from the DB; make them list-like for
        # pydantic serialization.
        if value is not None and not isinstance(value, list):
            value = list(value)
        return value


class SearchRequest(BaseModel):
    tenant_id: UUID
    embedding: List[float]
    top_k: int = Field(default=5, ge=1, le=100)
    threshold: float = Field(
        default=0.75,  # Cosine similarity threshold (face-api 128-d; 0.75+ is high confidence)
        ge=0.0,
        le=1.0,
    )

    @field_validator("embedding")
    @classmethod
    def _check_embedding_length(cls, value: List[float]) -> List[float]:
        return _validate_embedding_length(value)


class SearchResult(BaseModel):
    id: UUID
    person_id: Optional[str]
    score: float
    frame_url: Optional[str]
    created_at: datetime
