import logging
import os
import time
from sqlalchemy import create_engine, text
from sqlalchemy.exc import OperationalError
from sqlalchemy.orm import sessionmaker
from .models import Base, EMBEDDING_DIM

logger = logging.getLogger(__name__)

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

# ---------------------------------------------------------------------------
# R2 fix: bounded connection behaviour. Without connect_timeout a dead/black-
# holed Postgres makes every request thread hang for the kernel TCP timeout
# (minutes); with an unbounded pool a burst of traffic exhausts Postgres
# connections. All knobs are env-tunable.
# ---------------------------------------------------------------------------
DB_CONNECT_TIMEOUT_SECONDS = int(os.getenv("DB_CONNECT_TIMEOUT_SECONDS", "5"))
DB_POOL_SIZE = int(os.getenv("DB_POOL_SIZE", "10"))
DB_MAX_OVERFLOW = int(os.getenv("DB_MAX_OVERFLOW", "20"))
DB_POOL_TIMEOUT_SECONDS = int(os.getenv("DB_POOL_TIMEOUT_SECONDS", "5"))

_engine_kwargs: dict = {
    "pool_pre_ping": True,
    "pool_recycle": 1800,
    "pool_size": DB_POOL_SIZE,
    "max_overflow": DB_MAX_OVERFLOW,
    "pool_timeout": DB_POOL_TIMEOUT_SECONDS,
}
# connect_timeout is a libpq/psycopg2 keyword — only pass it for Postgres URLs
# so unit tests pointing at other drivers don't blow up on engine creation.
if DATABASE_URL.startswith("postgres"):
    _engine_kwargs["connect_args"] = {"connect_timeout": DB_CONNECT_TIMEOUT_SECONDS}

engine = create_engine(DATABASE_URL, **_engine_kwargs)
SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)


def _verify_embedding_dimension() -> None:
    """R4 fix: detect schema drift between the live pgvector column and
    EMBEDDING_DIM at startup, instead of failing cryptically per-request.

    For pgvector columns, pg_attribute.atttypmod stores the declared
    dimension directly.
    """
    with engine.connect() as conn:
        live_dim = conn.execute(
            text(
                "SELECT atttypmod FROM pg_attribute "
                "WHERE attrelid = 'person_embeddings'::regclass "
                "AND attname = 'embedding'"
            )
        ).scalar()

    if not isinstance(live_dim, int) or live_dim <= 0:
        logger.warning(
            "Could not introspect the live embedding column dimension "
            "(got %r); skipping schema-drift check.",
            live_dim,
        )
        return

    if live_dim != EMBEDDING_DIM:
        message = (
            f"SCHEMA DRIFT: person_embeddings.embedding in the database is "
            f"vector({live_dim}) but this service is configured for "
            f"EMBEDDING_DIM={EMBEDDING_DIM}. Every insert/search would fail. "
            f"Either migrate the column (ALTER TABLE ... TYPE vector({EMBEDDING_DIM})) "
            f"or set EMBEDDING_DIM={live_dim} to match the database."
        )
        logger.error(message)
        raise RuntimeError(message)

    logger.info("Embedding column dimension verified: vector(%d)", EMBEDDING_DIM)


def _pgvector_supports_hnsw(version: object) -> bool:
    """HNSW indexes shipped in pgvector 0.5.0."""
    try:
        parts = str(version).split(".")
        return (int(parts[0]), int(parts[1])) >= (0, 5)
    except (ValueError, IndexError, TypeError):
        logger.warning("Could not parse pgvector version %r; assuming HNSW support.", version)
        return True


def _ensure_vector_index() -> None:
    """R5 fix: without an ANN index every /search is a sequential scan over
    the whole table — latency grows linearly with enrollments until searches
    time out. Prefer HNSW (pgvector >= 0.5), fall back to IVFFlat.

    Index creation failure is logged but non-fatal: the service still works
    (slowly) without the index.
    """
    try:
        with engine.connect() as conn:
            version = conn.execute(
                text("SELECT extversion FROM pg_extension WHERE extname = 'vector'")
            ).scalar()

            if _pgvector_supports_hnsw(version):
                try:
                    conn.execute(
                        text(
                            "CREATE INDEX IF NOT EXISTS ix_person_embeddings_embedding_hnsw "
                            "ON person_embeddings USING hnsw (embedding vector_cosine_ops)"
                        )
                    )
                    conn.commit()
                    logger.info("Vector index ready: HNSW (pgvector %s).", version)
                    return
                except Exception as exc:  # noqa: BLE001 — fall back to ivfflat below
                    conn.rollback()
                    logger.warning("HNSW index creation failed (%s); falling back to IVFFlat.", exc)

            conn.execute(
                text(
                    "CREATE INDEX IF NOT EXISTS ix_person_embeddings_embedding_ivfflat "
                    "ON person_embeddings USING ivfflat (embedding vector_cosine_ops) "
                    "WITH (lists = 100)"
                )
            )
            conn.commit()
            logger.info("Vector index ready: IVFFlat (pgvector %s).", version)
    except Exception:  # noqa: BLE001
        logger.exception(
            "Failed to create a vector similarity index; /search will fall back "
            "to sequential scans (slow at scale)."
        )


def init_db(max_retries: int = 5, retry_delay_seconds: float = 3.0) -> None:
    """Create the pgvector extension, all tables, and the ANN index (idempotent).

    Also verifies that the live embedding column dimension matches
    EMBEDDING_DIM (raises with a clear message on schema drift).
    """
    last_error: Exception | None = None

    for attempt in range(1, max_retries + 1):
        try:
            with engine.connect() as conn:
                conn.execute(text("CREATE EXTENSION IF NOT EXISTS vector"))
                conn.commit()
            Base.metadata.create_all(bind=engine)
            _verify_embedding_dimension()
            _ensure_vector_index()
            return
        except OperationalError as exc:
            last_error = exc
            if attempt == max_retries:
                logger.exception("Database initialization failed after %s attempts", max_retries)
                raise
            logger.warning(
                "Database initialization failed on attempt %s/%s: %s",
                attempt,
                max_retries,
                exc,
            )
            time.sleep(retry_delay_seconds)
        except Exception:
            logger.exception("Unexpected error during database initialization")
            raise

    if last_error is not None:
        raise last_error


def get_db():
    """FastAPI dependency that yields a database session."""
    db = SessionLocal()
    try:
        yield db
    finally:
        db.close()
