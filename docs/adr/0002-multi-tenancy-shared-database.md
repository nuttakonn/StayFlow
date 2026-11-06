# ADR 0002: Multi-Tenancy Strategy — Shared Database with Row-Level Isolation

## Status
Accepted

## Context
StayFlow PMS serves multiple customer organizations in SaaS mode, while supporting dedicated single-tenant databases in On-premise mode.
We need an isolation strategy that is cost-effective, maintainable, straightforward to migrate, and securely isolated against cross-tenant data leaks.

Database-per-tenant introduces complex connection pooling, dynamic migration orchestration for thousands of databases, and resource inefficiencies for small properties.

## Decision
1. In SaaS mode, we use a **Shared Database, Shared Schema** model with an explicit `OrganizationId` on every organization-owned entity.
2. In On-premise mode, the customer runs the identical schema on their dedicated PostgreSQL instance.
3. Every authenticated HTTP request resolves the user's active `OrganizationId` and optional `PropertyId` from the cryptographically verified authentication token and session context.
4. Client-supplied tenant IDs (from query strings, path parameters, or body payloads) are **never** trusted as authorization evidence.
5. EF Core implements Global Query Filters (`HasQueryFilter`) and SaveChanges interceptors in the data access layer to enforce `OrganizationId` automatically across all read and write queries.
6. Property-scoped queries validate that the requested `PropertyId` belongs to the active `OrganizationId`.

## Consequences
### Positive
- Operational simplicity: single database migration step across the entire platform.
- Identical code paths for SaaS and On-premise deployments.
- Resource efficiency and predictable database connection pooling.

### Negative / Trade-offs
- Risk of developer error bypassing filters (e.g. `IgnoreQueryFilters()`). Mitigated by automated architecture and integration tests verifying cross-tenant 403/404 responses.
- If high-tier enterprise SaaS customers later require dedicated databases, a hybrid connection-routing strategy can be introduced via `ITenantConnectionProvider`.
