# Alpha-Surveillance: Platform Context & Architecture Guide

## 1. Project Overview
Alpha-Surveillance is an enterprise-grade, AI-powered computer vision platform designed for real-time violation detection and operational compliance. It monitors video streams to detect safety, security, and SOP (Standard Operating Procedure) violations, providing real-time alerts, auditing, and analytics.

**Core Vision:** A "Compliance Intelligence Platform" that scales from pilot deployments to smart-city-scale systems.

---

## 2. Architecture & Design Patterns
- **Microservices Architecture:** Decoupled services communicating via gRPC, SignalR, and REST.
- **Event-Driven Pipeline:** Vision results are pushed through AWS SQS to backend consumers.
- **Multi-Tenancy:** Robust tenant isolation using row-level security and `TenantId` resolution.
- **Orchestration:** Powered by **.NET Aspire** for local development, service discovery, and resource provisioning.
- **Security:** JWT-based authentication with RBAC and policy-based authorization.

---

## 3. Microservices Breakdown

### 3.1 Vision Inference Service (Python)
- **Role:** The "Producer" in the pipeline.
- **Functions:** Video ingestion, frame extraction, AI model inference (OpenCV/PyTorch), and violation classification.
- **Interactions:** 
    - Stores evidence frames in **AWS S3**.
    - Publishes violation metadata to **AWS SQS** (`violation-queue`).
- **Tech Stack:** Python 3.10+, PyTorch, OpenCV, Boto3.

### 3.2 Violation Management Service (.NET 8)
- **Role:** The "Brain" and primary domain service.
- **Functions:** SQS consumption, violation processing, business logic execution, tenant resolution, and notification routing.
- **Interactions:** Consumes from SQS, stores structured data in **PostgreSQL**, and triggers the Audit Service.
- **Tech Stack:** C#, ASP.NET Core, Entity Framework Core, AWS SDK.

### 3.3 Backend For Frontend (BFF) (.NET 8)
- **Role:** API Gateway and UI-optimized aggregator.
- **Functions:** Authentication enforcement, rate limiting, and real-time communication via **SignalR** hubs.
- **Interactions:** Aggregates data from downstream APIs (Violation, Audit) for the UI.
- **Tech Stack:** C#, SignalR, gRPC-web, Ocelot (or standard proxying).

### 3.4 Audit & Logging Service (.NET 8)
- **Role:** Compliance and traceability.
- **Functions:** Immutable event journaling, evidence tracking, and regulatory logging.
- **Storage:** Uses **TimescaleDB** (PostgreSQL extension) for time-series audit logs.

### 3.5 Human Re-ID Service (Python)
- **Role:** Identity tracking.
- **Functions:** Re-identifying individuals across multiple camera feeds to track movement and cumulative violations.

### 3.6 Surveillance UI (Next.js)
- **Role:** Operator and User interface.
- **Features:** Live "War Room" dashboards, heatmaps, analytics charts, and evidence playback.
- **Tech Stack:** React, Next.js, TailwindCSS, Lucide-React, Recharts.

---

## 4. Key Workflows ("Things We Do")

### A. The Violation Detection Pipeline
1. **Inference:** Python service detects a violation (e.g., "No Helmet").
2. **Evidence:** Frame is uploaded to S3; JSON payload is sent to SQS.
3. **Processing:** Violation Service consumes the SQS message, maps it to a `TenantId`, and persists it.
4. **Alerting:** BFF pushes a SignalR message to the Frontend Dashboard for real-time operator alerts.

### B. Multi-Tenant Isolation
- Every database table contains a `TenantId`.
- Middleware resolves `TenantId` from JWT claims or headers.
- Queries are automatically filtered by `TenantId` to prevent data leakage.

### C. Local Development Workflow
- **Aspire Orchestration:** Running `dotnet run` in the `AppHost` project starts all services, PostgreSQL, Redis, and provides an interactive dashboard.
- **Infrastructure-as-Code:** SQS queues and cloud resources are provisioned/configured automatically by the AppHost during startup.

---

## 5. Technology Stack Summary
- **Languages:** C# (.NET 8), Python 3.10+, TypeScript.
- **Databases:** PostgreSQL (Operational), TimescaleDB (Audit), Redis (Caching).
- **Cloud (AWS):** SQS (Queuing), S3 (Storage), IAM (Security).
- **Orchestration:** .NET Aspire, Docker Desktop.
- **Frontend:** Next.js 14+, SignalR (WebSockets).

---

## 6. How to Assist (Gemini Guidelines)
When assisting with this codebase:
- **C# Patterns:** Use Clean Architecture, MediatR, and FluentValidation.
- **Python Patterns:** Follow PEP8, use type hinting, and ensure Boto3 clients are properly managed.
- **Security:** Always verify `TenantId` presence in queries and mutations.
- **Infrastructure:** Reference Aspire's `AppHost` for service discovery and environment variable injection.
