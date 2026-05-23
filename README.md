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
