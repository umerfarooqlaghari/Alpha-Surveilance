import os
from sqlalchemy import create_engine, text
from sqlalchemy.orm import sessionmaker
from .models import Base

# ---------------------------------------------------------------------------
# Connection string resolution
#   1. DATABASE_URL env var  (Docker / Aspire / CI)
#   2. .env file             (standalone local dev)
# ---------------------------------------------------------------------------
def _resolve_db_url() -> str:
    url = os.getenv("DATABASE_URL")
    if url:
        return url

    # Try loading from a .env file in the project root (one level up from app/)
    try:
        from dotenv import load_dotenv
        _env_path = os.path.join(os.path.dirname(__file__), "..", ".env")
        load_dotenv(dotenv_path=_env_path)
        url = os.getenv("DATABASE_URL")
    except ImportError:
        pass

    if not url:
        raise RuntimeError(
            "DATABASE_URL is not set.\n"
            "  • Standalone: copy .env.example → .env and fill in your Postgres URL.\n"
            "  • Aspire: the AppHost injects it automatically via ConnectionStrings:reid."
        )
    return url


DATABASE_URL = _resolve_db_url()

engine = create_engine(DATABASE_URL)
SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)


def init_db() -> None:
    """Create the pgvector extension and all tables (idempotent)."""
    with engine.connect() as conn:
        conn.execute(text("CREATE EXTENSION IF NOT EXISTS vector"))
        conn.commit()
    Base.metadata.create_all(bind=engine)


def get_db():
    """FastAPI dependency that yields a database session."""
    db = SessionLocal()
    try:
        yield db
    finally:
        db.close()
