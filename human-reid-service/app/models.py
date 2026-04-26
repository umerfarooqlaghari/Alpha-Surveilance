from sqlalchemy import Column, String, Integer, DateTime, ForeignKey, JSON
from sqlalchemy.dialects.postgresql import UUID
from sqlalchemy.ext.declarative import declarative_base
from pgvector.sqlalchemy import Vector
import uuid
from datetime import datetime

Base = declarative_base()

class PersonEmbedding(Base):
    __tablename__ = "person_embeddings"

    id = Column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4)
    tenant_id = Column(UUID(as_uuid=True), index=True, nullable=False)
    
    # The actual vector (embedding). 512 is common for ReID models like OSNet
    embedding = Column(Vector(512), nullable=False)
    
    # Metadata
    person_id = Column(String(100), index=True) # Optional link to a known person/employee
    camera_id = Column(String(100), index=True)
    frame_url = Column(String(500))
    metadata_json = Column(JSON, default={})
    
    created_at = Column(DateTime, default=datetime.utcnow)
