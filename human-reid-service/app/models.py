import os
import uuid
from datetime import datetime, timezone

from sqlalchemy import Column, String, DateTime, JSON
from sqlalchemy.dialects.postgresql import UUID
from sqlalchemy.orm import declarative_base
from pgvector.sqlalchemy import Vector

# ---------------------------------------------------------------------------
# R4 fix: single source of truth for the embedding dimensionality.
# The vision-inference-service sends 128-d face embeddings (dlib /
# face_recognition / face-api.js), NOT 512-d OSNet vectors. Default matches
# the actual caller; override via EMBEDDING_DIM only if the whole pipeline
# (callers + existing DB column) is migrated together.
# ---------------------------------------------------------------------------
EMBEDDING_DIM: int = int(os.getenv("EMBEDDING_DIM", "128"))

Base = declarative_base()


def _utcnow() -> datetime:
    """Timezone-aware UTC timestamp (G5 fix: datetime.utcnow is deprecated).

    The column stays a plain DateTime (timestamp without time zone), so the
    stored value is unchanged — only the deprecated API call is replaced.
    """
    return datetime.now(timezone.utc).replace(tzinfo=None)


class PersonEmbedding(Base):
    __tablename__ = "person_embeddings"

    id = Column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4)
    tenant_id = Column(UUID(as_uuid=True), index=True, nullable=False)

    # The face embedding vector. 128-d matches dlib/face-api.js encodings
    # produced by vision-inference-service (see EMBEDDING_DIM above).
    embedding = Column(Vector(EMBEDDING_DIM), nullable=False)

    # Metadata
    person_id = Column(String(100), index=True)  # Optional link to a known person/employee
    camera_id = Column(String(100), index=True)
    # G5 fix: mutable default {} was shared across instances; use dict factory.
    frame_url = Column(String(500))
    metadata_json = Column(JSON, default=dict)

    created_at = Column(DateTime, default=_utcnow)
