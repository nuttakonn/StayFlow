# StayFlow PMS

> **StayFlow PMS** is a unified, cloud-based Property Management System (PMS) designed for **hotels and apartments**, supporting both **SaaS (Multi-Tenant)** and **On-Premise (Dedicated Instance)** deployments from **one shared codebase**.

---

## 1. Architecture Overview

- **Backend:** ASP.NET Core 10 Web API (Modular Monolith)
- **Database:** PostgreSQL 17 via EF Core (Shared schema, explicit `OrganizationId` tenant isolation)
- **Frontend:** React 19 + TypeScript + Vite + Tailwind CSS + TanStack Query + transitions.dev motion tokens
- **Multi-Tenancy:** Row-level tenant scope enforced at data access boundary with property scoping
- **Inventory Engine:** Buildings, Floors, Room Types, Rooms with decoupled Operational Status (Clean, Dirty, Cleaning, Inspected, OutOfService, Maintenance)
- **Branding:** 3-tier white-label resolver (`Property Override` $\succ$ `Organization Branding` $\succ$ `Platform Default`)

---

## 2. Prerequisites

Ensure the following tools are installed on your machine:
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (`dotnet --version` >= 10.0)
- [Node.js](https://nodejs.org/) (`node -v` >= 20.0, npm >= 10.0)
- [Docker & Docker Compose](https://www.docker.com/)

---

## 3. Quick Start / Local Development

### 1. Start Local PostgreSQL Database
```bash
docker compose up -d
```
This starts PostgreSQL 17 on `localhost:5432` with database `stayflow_db`.

### 2. Configure Environment Variables
Copy `.env.example` to `.env` (optional for custom overrides; defaults work out of the box):
```bash
cp .env.example .env
```

### 3. Run Backend API
```bash
dotnet run --project src/backend/StayFlow.Api
```
The API server starts on `http://localhost:5000`:
- **Swagger UI:** `http://localhost:5000/swagger` (Interactive documentation & testing with JWT Bearer support)
- **OpenAPI Schema:** `http://localhost:5000/openapi/v1.json`
- **Health Check:** `http://localhost:5000/healthz`

### 4. Run Frontend Web App
In a separate terminal:
```bash
cd src/frontend/stayflow-web
npm install
npm run dev
```
The frontend starts on `http://localhost:5173` with automatic API proxying to `http://localhost:5000`.

---

## 4. Postman Collection & API Testing

Pre-configured Postman files are located under `postman/`:
- **Collection:** [`postman/StayFlow_PMS.postman_collection.json`](postman/StayFlow_PMS.postman_collection.json)
- **Environment:** [`postman/StayFlow_PMS.postman_environment.json`](postman/StayFlow_PMS.postman_environment.json)
- **API Spec (OpenAPI 3.1):** [`docs/api/openapi-v1.json`](docs/api/openapi-v1.json)

### How to use in Postman:
1. Open Postman $\rightarrow$ Click **Import** $\rightarrow$ Select both `StayFlow_PMS.postman_collection.json` and `StayFlow_PMS.postman_environment.json`.
2. Select the active environment: **StayFlow PMS - Local**.
3. Run **01. Authentication -> Register User & Organization** or **Login**:
   - The test script will **automatically extract** the JWT token, refresh token, and `organizationId` into environment variables (`{{authToken}}`, `{{organizationId}}`).
4. Subsequent requests in Organizations, Properties, Branding, and Audit will automatically use `{{authToken}}` and `X-Organization-Id` without manual copy-pasting.

---

## 5. Testing & Verification

### Run All Backend Tests (Unit & Integration)
```bash
dotnet test StayFlow.slnx
```

### Run Frontend Build
```bash
cd src/frontend/stayflow-web
npm run build
```

---

## 5. Solution Structure

```text
StayFlowPMS/
├── GEMINI.md                    # Primary product & engineering instruction blueprint
├── README.md                    # Setup, run, and verification guide
├── StayFlow.slnx                # Modern XML-based .NET Solution file
├── docker-compose.yml           # Local dependencies (PostgreSQL 17)
├── docs/
│   └── adr/                     # Architecture Decision Records
│       ├── 0001-modular-monolith-architecture.md
│       ├── 0002-multi-tenancy-shared-database.md
│       ├── 0003-authentication-and-authorization.md
│       ├── 0004-date-time-and-timezone-handling.md
│       ├── 0005-money-and-currency-precision.md
│       └── 0006-unified-saas-and-onpremise-deployment.md
├── src/
│   ├── backend/
│   │   ├── StayFlow.Api/        # ASP.NET Core 10 Web API host & endpoints
│   │   ├── StayFlow.BuildingBlocks/ # Shared abstractions, Result pattern, Tenancy, Branding
│   │   └── Modules/             # Domain modules (Identity, Properties, Reservations, Folios)
│   └── frontend/
│       └── stayflow-web/        # React 19 + TypeScript + Vite SPA
└── tests/
    └── backend/
        ├── UnitTests/           # Unit tests (Result, Branding, Domain invariants)
        └── IntegrationTests/    # API integration tests (Health checks, Multi-tenancy)
```

---

## 6. Architecture Decision Records (ADRs)

Key architectural decisions are documented under `docs/adr/`:
- [ADR-0001: Modular Monolith Architecture](docs/adr/0001-modular-monolith-architecture.md)
- [ADR-0002: Multi-Tenancy Strategy](docs/adr/0002-multi-tenancy-shared-database.md)
- [ADR-0003: Authentication and Authorization Strategy](docs/adr/0003-authentication-and-authorization.md)
- [ADR-0004: Date, Time, and Timezone Handling Strategy](docs/adr/0004-date-time-and-timezone-handling.md)
- [ADR-0005: Money and Currency Precision Strategy](docs/adr/0005-money-and-currency-precision.md)
- [ADR-0006: Unified SaaS and On-Premise Single Codebase](docs/adr/0006-unified-saas-and-onpremise-deployment.md)
- [ADR-0007: Reservation Conflict Prevention Strategy](docs/adr/0007-reservation-conflict-prevention-strategy.md)
- [ADR-0008: Subscription, Entitlement, and On-Premise Licensing Strategy](docs/adr/0008-subscription-entitlement-and-licensing-strategy.md)

---

## 7. Current Implementation Status

- **Phases 1–6** (platform foundation, rooms, availability & hotel reservations, stay operations, billing & folios, apartment rental agreements) are implemented with passing integration test suites (`Phase1IntegrationTests.cs` – `Phase6IntegrationTests.cs`).
- **Phase 7** (SaaS readiness) is in progress: subscription plans, capability-based entitlements, and server-side enforcement (`properties.max`) are implemented and covered by `Phase7IntegrationTests.cs`. See [ADR-0008](docs/adr/0008-subscription-entitlement-and-licensing-strategy.md) for what's deferred — notably **On-Premise signed-license verification is not implemented** and must not be assumed to be enforced.
- **Known gap:** there are no EF Core migrations yet; PostgreSQL schema provisioning is not automated (see ADR-0008). The InMemory provider used for local dev/tests is unaffected.
