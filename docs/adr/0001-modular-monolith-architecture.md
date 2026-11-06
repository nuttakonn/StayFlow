# ADR 0001: Modular Monolith Architecture

## Status
Accepted

## Context
StayFlow PMS needs to serve both SaaS (multi-tenant cloud) and on-premise deployments using one single codebase. The system encompasses distinct business domains: Identity & Tenancy, Properties & Room Inventory, Reservations & Availability, Stay Operations, Folios & Payments, Housekeeping, and Apartment Lease Agreements.

Choosing microservices prematurely would incur high operational overhead, distributed transaction complexity (especially across inventory booking and financial posting), network latency, and deployment complexity for on-premise customers. Conversely, a tangled monolithic architecture ("big ball of mud") leads to high coupling, unintended database queries bypassing business logic, and high regression risk.

## Decision
We adopt a **Modular Monolith** architecture:
1. Backend is built in a single deployable ASP.NET Core 10 Web API application (`StayFlow.Api`).
2. Modules (`src/backend/Modules/*`) own their internal domain models, business validation, and data persistence.
3. Modules communicate via explicit application interfaces, use case handlers, or in-process domain events.
4. Direct cross-module database foreign keys or direct querying across module boundaries without passing through contracts is prohibited.
5. Shared utilities, base entities, and common patterns reside in `StayFlow.BuildingBlocks`.

## Consequences
### Positive
- Single process deployment: easy to package for Docker, single-server SaaS, and offline On-premise deployments.
- Strong compile-time module boundaries without network serialization costs or distributed transaction nightmares.
- Clear migration path to microservices if individual business capabilities (e.g. Channel Manager, High-Scale Availability Engine) require independent scaling in the future.

### Negative / Trade-offs
- Requires strict enforcement of boundaries through architecture tests (`StayFlow.ArchitectureTests`) and code reviews to prevent leaking dependencies between modules.
