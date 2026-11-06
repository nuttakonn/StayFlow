# ADR 0003: Authentication and Authorization Strategy

## Status
Accepted (Provisional on OIDC vs Identity integration)

## Context
StayFlow PMS requires authentication for staff, property managers, and administrative users across web and potentially mobile interfaces. Users belong to Organizations and may have specific role-based permissions scoped to the whole organization or individual properties.

## Decision
1. **Core Authentication:** Use ASP.NET Core Identity for password hashing (PBKDF2/Argon2), secure token issuance, and account lifecycle management, with capability to federate with external OIDC providers (e.g., Google, Microsoft Entra, Keycloak).
2. **Session / Token Delivery:**
   - Default: Secure HTTP-Only, SameSite=Strict cookies for web browser sessions with CSRF protection, preventing XSS-based token theft.
   - API / Headless / Mobile: Short-lived Bearer JWTs with refresh token rotation stored in a revocable backend token store.
3. **Authorization Model:**
   - **Tenant Scope:** Active `OrganizationId` must be valid and active.
   - **Property Scope:** Active `PropertyId` must belong to the active organization and user must have access.
   - **Role & Permissions:** Role-Based Access Control (RBAC) combined with fine-grained capability permissions (e.g. `Rooms.View`, `Reservations.Create`, `Folios.Settle`, `Branding.Manage`).
4. **Server-Side Enforcement:** Authorization is enforced in ASP.NET Core endpoint filters / authorization handlers. Hiding a button in React is UX only, never security.

## Consequences
### Positive
- Works equally well in offline On-premise setups (where third-party cloud auth is unreachable) and multi-tenant SaaS environments.
- High security: credentials and refresh tokens are protected from browser script access.
- Flexible permission granularity for hotel staff (e.g. Front Desk, Housekeeping, General Manager, Accountant).

### Negative / Trade-offs
- Refresh token rotation and cookie management require CSRF protection and proper CORS configuration.
