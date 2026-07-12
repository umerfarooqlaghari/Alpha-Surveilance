# Human Re-ID Service

A FastAPI microservice that stores and searches **128-dimensional face embeddings** (dlib / `face_recognition` / face-api.js encodings, as produced by `vision-inference-service`) using [pgvector](https://github.com/pgvector/pgvector) for cosine-similarity re-identification.

The dimensionality has a single source of truth: the `EMBEDDING_DIM` env var (default `128`). At startup the service introspects the live pgvector column and refuses to come up (with a clear log message) if the database schema disagrees.

---

## Running Standalone (Local Dev)

### Prerequisites
- Python 3.11+
- PostgreSQL with the `pgvector` extension  
  _(easiest: `docker run -p 5432:5432 -e POSTGRES_PASSWORD=postgres ankane/pgvector`)_

### Steps

```bash
# 1. Copy the env template
cp .env.example .env
# Edit DATABASE_URL in .env to point at your Postgres instance

# 2. Start the service (handles venv + deps automatically)
./run.sh
```

The API will be available at:
- **http://localhost:8001** — Service root
- **http://localhost:8001/docs** — Swagger UI
- **http://localhost:8001/health** — Health check

---

## Running via Aspire (Integrated)

The service is already registered in the AppHost (`surveilance-app-host/AppHost1/Program.cs`).  
The `DATABASE_URL` is injected automatically from the `ConnectionStrings:reid` entry in `appsettings.development.json`.

Just run the AppHost and the ReID service starts as a Docker container alongside the rest of the stack.

---

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/health` | Service health check |
| `POST` | `/embeddings` | Store a person embedding |
| `POST` | `/search` | Find similar embeddings (cosine similarity) |
| `DELETE` | `/embeddings/person/{person_id}?tenant_id=<uuid>` | Remove all embeddings for a person |

### Store an Embedding
```json
POST /embeddings
{
  "tenant_id": "uuid",
  "embedding": [0.1, 0.2, ...],   // 128 floats (EMBEDDING_DIM)
  "person_id": "emp-001",          // optional
  "camera_id": "cam-lobby",        // optional
  "frame_url": "https://...",      // optional
  "metadata_json": {}              // optional
}
```

### Search by Embedding
```json
POST /search
{
  "tenant_id": "uuid",
  "embedding": [0.1, 0.2, ...],   // 128 floats (EMBEDDING_DIM)
  "top_k": 5,                      // max results (1-100)
  "threshold": 0.75                // min cosine similarity (0-1)
}
```
Returns at most one (best) match per `person_id`, ordered by score descending. Wrong-length embeddings are rejected with `422`.

---

## Database Schema

| Column | Type | Notes |
|--------|------|-------|
| `id` | UUID | Primary key |
| `tenant_id` | UUID | Tenant isolation |
| `embedding` | vector(128) | pgvector column (dimension = `EMBEDDING_DIM`) |
| `person_id` | string | Optional known identity |
| `camera_id` | string | Source camera |
| `frame_url` | string | S3/CDN URL of the captured frame |
| `metadata_json` | JSON | Arbitrary metadata |
| `created_at` | datetime | Insertion timestamp |

An HNSW cosine index (`ix_person_embeddings_embedding_hnsw`) is created automatically at startup (IVFFlat fallback for pgvector < 0.5).

---

## Tuning (env vars)

| Var | Default | Purpose |
|-----|---------|---------|
| `EMBEDDING_DIM` | `128` | Embedding dimensionality (single source of truth) |
| `DB_CONNECT_TIMEOUT_SECONDS` | `5` | Postgres connect timeout |
| `DB_POOL_SIZE` / `DB_MAX_OVERFLOW` / `DB_POOL_TIMEOUT_SECONDS` | `10` / `20` / `5` | Connection pool sizing |
| `DB_RETRY_INTERVAL_SECONDS` | `15` | Min interval between degraded-mode DB recovery attempts |

> Note: endpoint authentication is intentionally out of scope for this service revision — deploy it on a private network / behind the internal gateway.
