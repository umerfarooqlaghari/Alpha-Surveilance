# Alpha Surveillance - Local Run Guide

This repository contains the Alpha Surveillance system:

- `surveilance-UI`: Next.js frontend
- `alpha-surveilance-bff`: .NET BFF used by the frontend
- `violation-management-service-api`: .NET violation management API
- `audit-services-api`: .NET audit API and gRPC audit service
- `vision-inference-service`: Python/FastAPI vision inference service
- `human-reid-service`: Python/FastAPI human re-identification service
- `surveilance-app-host/AppHost1`: .NET Aspire AppHost for local orchestration

The recommended local workflow is to run everything through the Aspire AppHost.

## 1. Prerequisites

Install these before starting:

1. Docker Desktop
2. .NET SDK that supports `net10.0`
3. Node.js and npm
4. AWS CLI
5. Git

Python is only required if you run the Python services manually. Through Aspire, the Python services are built and run with Docker.

## 2. Clone And Open The Project

```powershell
cd F:\umer-cv
cd Alpha-Surveilance
```

If you cloned somewhere else, run all commands from your own `Alpha-Surveilance` folder.

## 3. Configure AWS

The Aspire AppHost uses the AWS `default` profile and provisions an SQS queue from `surveilance-app-host/AppHost1/sqs-template.json`.

```powershell
aws configure
```

Use the same AWS region configured by the project, currently `us-east-1`.

Your AWS user needs permission for:

- CloudFormation
- SQS
- S3 bucket read/write access for the configured bucket

## 4. Configure Local Secrets

Do not commit real secrets to Git.

The AppHost requires these settings:

- `InternalApi:ApiKey`
- `Roboflow:ApiKey`
- `S3Config:BucketName`
- `AWS:Region`

Set them with .NET user secrets from the AppHost folder:

```powershell
cd surveilance-app-host\AppHost1

dotnet user-secrets set "InternalApi:ApiKey" "<your-internal-api-key>"
dotnet user-secrets set "Roboflow:ApiKey" "<your-roboflow-api-key>"
dotnet user-secrets set "S3Config:BucketName" "<your-s3-bucket-name>"
dotnet user-secrets set "AWS:Region" "us-east-1"
```

Also verify the API configuration files contain valid local values:

- `alpha-surveilance-bff/appsettings.development.json`
- `violation-management-service-api/violation-management-api/appsettings.json`
- `audit-services-api/audit-api/appsettings.json`

The `Jwt` settings must match between the BFF, violation API, and audit API:

- `Jwt:SecretKey`
- `Jwt:Issuer`
- `Jwt:Audience`

The internal API key should also be the same value everywhere it is used.

## 5. Install Frontend Dependencies

The AppHost runs the frontend with `npm run dev`, so install packages once:

```powershell
cd F:\umer-cv\Alpha-Surveilance\surveilance-UI
npm install
```

## 6. Restore .NET Dependencies

```powershell
cd F:\umer-cv\Alpha-Surveilance\surveilance-app-host\AppHost1
dotnet restore
```

## 7. Start Docker Desktop

Open Docker Desktop and wait until it is fully running.

The AppHost creates local containers for:

- PostgreSQL violation database on port `5432`
- TimescaleDB audit database on port `5433`
- pgvector Re-ID database on port `5434`
- Redis on port `6379`
- Vision inference service on port `8000`
- Human Re-ID service on port `8001`

Make sure these ports are free before starting.

## 8. Run The Whole System

Start everything from the AppHost:

```powershell
cd F:\umer-cv\Alpha-Surveilance\surveilance-app-host\AppHost1
dotnet run
```

On first run, Docker may take several minutes to pull images, build Python service images, and download AI dependencies.

Open the Aspire Dashboard URL printed in the terminal. It is usually one of:

- `https://localhost:17109`
- `http://localhost:15051`

Use the Aspire Dashboard to check service status and logs.

## 9. Open The App

After the services are healthy, open:

```text
http://localhost:3000
```

Useful local service URLs:

```text
Frontend:             http://localhost:3000
BFF Swagger:          http://localhost:5002/swagger
Violation API:        http://localhost:5001/swagger
Audit API:            http://localhost:5003/swagger
Vision health:        http://localhost:8000/health
Vision test page:     http://localhost:8000
Human Re-ID health:   http://localhost:8001/health
```

The violation API applies migrations and seeds development data at startup. The seeded development SuperAdmin account is defined in:

```text
violation-management-service-api/violation-management-api/Data/Seeds/DatabaseSeeder.cs
```

## 10. Quick Health Checks

Run these in a new terminal after the AppHost is running:

```powershell
curl http://localhost:5001/health
curl http://localhost:8000/health
curl http://localhost:8001/health
curl http://localhost:5002/api/debug/config
```

## 11. Stop The Project

Press `Ctrl+C` in the AppHost terminal.

If Docker containers keep running, stop them from Docker Desktop or from the Aspire Dashboard.

## Optional: Run Only Infrastructure With Docker Compose

The root `docker-compose.yaml` starts only the main databases and Redis:

```powershell
cd F:\umer-cv\Alpha-Surveilance
docker compose up -d
```

This is useful if you want to run APIs manually from separate terminals. It does not start the frontend, .NET APIs, Python services, or the pgvector Re-ID database.

To stop the compose services:

```powershell
docker compose down
```

## Violation Video Clips

When a violation fires, the vision service encodes a ~3s H.264 MP4 from a rolling
in-memory frame buffer, uploads it to S3, and PATCHes the URL onto the violation.
The violations table serves it as a 24 h pre-signed `videoClipUrl`.

Settings (all optional; environment variables on the vision service):

| Variable | Default | Purpose |
| --- | --- | --- |
| `VIOLATION_CLIPS_ENABLED` | `true` | Master switch. When off — or when `S3_BUCKET_NAME` / `AWS_REGION` are unset — the per-camera frame buffer is **not allocated**, saving ~59MB RSS per camera and one full frame decode per buffered frame. |
| `CLIP_PRE_ROLL_SECONDS` / `CLIP_POST_ROLL_SECONDS` | `1.5` / `1.5` | Window captured around the violation instant. |
| `CLIP_BUFFER_FPS` | `15.0` | Frames per second held in the buffer. |
| `CLIP_MAX_DIMENSION` | `720` | Longest edge of buffered frames. |
| `CLIP_WORKERS` | `4` | Encoder thread pool size. |
| `CLIP_MAX_INFLIGHT` | `12` | Clip jobs queued before new ones are shed. A job that runs after its frames have aged out of the buffer cannot produce the right footage, so it is dropped rather than queued. |
| `CLIP_PATCH_MAX_ATTEMPTS` | `5` | Retries for the `VideoClipPath` PATCH (covers the case where the violation POST is still in the DLQ). |
| `CLIP_SSE_ALGORITHM` | `AES256` | Server-side encryption for uploaded clips. |
| `CLIP_SSE_KMS_KEY_ID` | _(unset)_ | Set to upgrade clips to SSE-KMS. |
| `CLIP_RETENTION_DAYS` | `90` | Written into the object tag consumed by the lifecycle rule below. |

### /analyze annotated review video

`POST /analyze` accepts an image or a video (`.mp4/.mov/.avi/.mkv/.dav`) and runs
every frame through the production pipeline. For video uploads it also renders a
single annotated MP4 and returns it as `annotated_video_url`.

The overlay is three-tier, so a reviewer sees the pipeline's *reasoning* rather
than only its verdict:

| Colour | Tier | Meaning |
| --- | --- | --- |
| steel | `detection` | the detector saw it |
| amber | `rule-pass` | passed rule evaluation, but the state machine did not act (cooldown, hysteresis, dwell not yet met) |
| red | `violation` | fired this frame — tagged `NEW` or `UPD`, plus a red frame border on a new violation |

Each frame also carries a HUD (frame number, media timestamp, camera, and
per-frame det / rule-pass / fired counts with a running total). The amber tier is
what answers "why didn't this fire?" without re-running the job.

Extra form fields:

| Field | Default | Purpose |
| --- | --- | --- |
| `render_video` | `true` | Set false to skip rendering and return JSON only. |

Settings:

| Variable | Default | Purpose |
| --- | --- | --- |
| `ANALYZE_RENDER_VIDEO` | `true` | Master switch for the annotated render. |
| `ANALYZE_RENDER_MAX_DIMENSION` | `1280` | Longest edge of the output video. |
| `ANALYZE_RENDER_CRF` | `23` | x264 quality (lower = larger, better). |
| `ANALYZE_RENDER_PRESET` | `veryfast` | x264 speed/size tradeoff. |
| `ANALYZE_RENDER_TIMEOUT_SECONDS` | `120` | Whole-video encode budget. |

Notes:

- Frames are streamed into FFmpeg as they are processed — nothing accumulates in
  memory. A 300-frame 1080p render would otherwise hold ~1.8GB.
- The output plays at `source_fps / frame_stride`, so a strided render keeps the
  source's real-time duration.
- Rendering costs roughly 0.6s per 45 frames at 720p on top of inference.
- Rendering is **skipped, never fatal**: with `TESTING_MODE` on or S3 unset,
  `annotated_video_url` is `null` and `annotated_video_note` says why. A drawing
  or encoding fault never fails the analysis — the JSON verdict is the primary
  product.
- Renders are uploaded under `analyze/{tenant}/{camera}/{date}/` and tagged
  `retention=analyze-render`, so the lifecycle rule below can expire them on a
  different schedule from violation clips.

### Required S3 lifecycle rule

Clips are video of identifiable people and are never deleted by the application —
marking a violation as a false positive does not remove its clip. **Configure an
S3 lifecycle rule** so footage expires on your retention schedule. Every clip is
tagged `retention=violation-clip`, so the rule can target clips without touching
the still frames in the same prefix:

```json
{
  "Rules": [{
    "ID": "expire-violation-clips",
    "Status": "Enabled",
    "Filter": { "Tag": { "Key": "retention", "Value": "violation-clip" } },
    "Expiration": { "Days": 90 }
  }]
}
```

Keep `Days` and `CLIP_RETENTION_DAYS` in sync — the tag is documentation for
operators, the rule is what actually deletes.

### Monitoring

`/metrics` exposes `vision_violation_clip_total{outcome=...}`
(`uploaded` / `encode_fail` / `upload_fail` / `patch_fail` / `insufficient_frames`
/ `rejected_saturated`), `vision_violation_clip_inflight`, and
`vision_violation_clip_duration_seconds`. A rising `insufficient_frames` means
violations are reaching the recorder later than the buffer window — usually slow
re-identification; increase `CLIP_BUFFER_HEADROOM_SECONDS`.


## Docker Compose Environment

The compose files substitute variables from the `.env` in the **project
directory** — the directory you run `docker compose` from, not the repo root.
Running `human-reid-service/docker-compose.edge.yaml` from inside
`human-reid-service/` therefore reads `human-reid-service/.env`, which is a
different file from the root `.env`.

Required (no defaults — compose fails fast with a message if unset):

| Variable | Used by | Notes |
| --- | --- | --- |
| `POSTGRES_PASSWORD` | `docker-compose.yaml` | Initialises the violation + audit DB containers. |
| `EDGE_DB_PASSWORD` | `.edge.yaml`, `.prod.yaml` | Initialises the edge pgvector DB. **Generate a distinct value per edge box** — there is deliberately no default. |
| `INTERNAL_API_KEY` | `docker-compose.yaml` | Must match `InternalApi:ApiKey` in the .NET services. |
| `IMAGE_REGISTRY_ORG` | `.prod.yaml` | GitHub org/user owning the GHCR images. |

These are **container-init** passwords: Postgres applies `POSTGRES_PASSWORD`
only when it initialises an empty data directory. Changing the value later does
nothing to an existing volume — drop the volume (`docker compose down -v`) or
`ALTER USER postgres WITH PASSWORD ...` inside the running container.

Both local database services use `pgvector/pgvector:pg16` (a superset of
`postgres:16`) because `human-reid` runs `CREATE EXTENSION IF NOT EXISTS vector`
against the database it is pointed at. Substituting a plain `postgres` image
leaves human-reid permanently degraded: its lazy-recovery path keeps the
container alive while `/health` returns 503 forever, which looks like a network
fault rather than a missing extension.


## Troubleshooting

If the AppHost fails immediately with missing configuration, check that these user secrets exist in `surveilance-app-host/AppHost1`:

```powershell
dotnet user-secrets list
```

If the frontend cannot reach the backend, verify the BFF is running at:

```text
http://localhost:5002
```

The frontend uses `NEXT_PUBLIC_BFF_URL`, and the AppHost sets it to `http://localhost:5002`.

If the violation API cannot start, check:

- PostgreSQL port `5432` is free
- `Jwt` values are present
- `InternalApi:ApiKey` is present
- AWS credentials are valid

If the vision service starts slowly, wait for Docker image build and AI model downloads to finish. This is expected on the first run.

If SQS provisioning fails, confirm the AWS `default` profile has CloudFormation and SQS permissions and that the `violation-queue` name does not conflict with an existing queue in the same account and region.
