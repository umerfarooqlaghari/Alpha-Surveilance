# Human Re-ID Service

A FastAPI microservice that stores and searches **512-dimensional person embeddings** using [pgvector](https://github.com/pgvector/pgvector) for cosine-similarity re-identification.

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
| `DELETE` | `/embeddings/{tenant_id}/{person_id}` | Remove all embeddings for a person |

### Store an Embedding
```json
POST /embeddings
{
  "tenant_id": "uuid",
  "embedding": [0.1, 0.2, ...],   // 512 floats
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
  "embedding": [0.1, 0.2, ...],   // 512 floats
  "top_k": 5,                      // max results
  "threshold": 0.75                // min cosine similarity (0-1)
}
```

---

## Database Schema

| Column | Type | Notes |
|--------|------|-------|
| `id` | UUID | Primary key |
| `tenant_id` | UUID | Tenant isolation |
| `embedding` | vector(512) | pgvector column |
| `person_id` | string | Optional known identity |
| `camera_id` | string | Source camera |
| `frame_url` | string | S3/CDN URL of the captured frame |
| `metadata_json` | JSON | Arbitrary metadata |
| `created_at` | datetime | Insertion timestamp |
