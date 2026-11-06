# ADR-0008: Subscription, Entitlement, and On-Premise Licensing Strategy

## Status

Partially implemented. SaaS subscription + capability entitlements are implemented
and tested. On-Premise signed-license verification is explicitly **not** implemented
(see "Deferred" below) and must not be assumed to exist.

## Context

CLAUDE.md section 21 requires capability-based entitlements for SaaS and signed
licenses for On-Premise, decoupled from hardcoded plan names, with server-side
enforcement (frontend visibility is never a security boundary).

## Decision

### Capability keys

A fixed set of string capability keys (`EntitlementKeys`) mirrors CLAUDE.md 21.2
exactly (`hotel.reservations`, `apartment.rentals`, `branding.customDomain`, etc.).
Numeric limits (`users.max`, `properties.max` in the spec) are modeled as typed
fields (`MaxUsers`, `MaxProperties`) on `SubscriptionPlan`/`EffectiveEntitlements`
rather than as keys, since they carry a value rather than being on/off.

### Entities

- `SubscriptionPlan` + `PlanEntitlement` — the platform catalog. **Not** tenant-owned;
  no `OrganizationId`. Seeded idempotently at startup by `SubscriptionPlanSeeder`
  (three plans: `starter`, `professional`, `enterprise`) rather than via EF Core
  `HasData`, because this repository has no EF Core migrations yet (see "Known gap"
  below) and `HasData` is only guaranteed to apply through `Migrate()` / the
  InMemory provider's implicit schema creation — it would silently do nothing against
  a hand-provisioned PostgreSQL database.
- `Subscription` — one per SaaS organization. Created only for
  `DeploymentMode.SaaS`; On-Premise organizations never get one.
- `SubscriptionHistory` — append-only business-level narrative of subscription
  lifecycle events, distinct from the generic field-diff `AuditEvent` that
  `StayFlowDbContext.SaveChangesAsync` already records for every entity change.
- `SubscriptionItems` and `UsageRecords` (named in the spec) are deferred: there is
  no billing-provider integration yet, so there is nothing to meter or line-item.

### Resolution: `IEntitlementService`

The single place every business module must call to check feature access
server-side. Resolution is always computed fresh (never cached) from the current
`Subscription` + `SubscriptionPlan`, or from the deployment-mode branch for
On-Premise, so a plan change or cancellation takes effect on the very next request.

### Provisional policy choices (deliberately simple defaults, isolated so they can
change without touching this entity's shape)

- An **expired trial** grants nothing (hard stop) rather than a grace period.
- **PastDue** still grants access (soft grace period) until the subscription is
  explicitly cancelled.
- A missing `SubscriptionPlan` row for a `Subscription` that references it (data
  drift) fails closed (`Status = "PlanNotFound"`, `IsActive = false`) rather than
  guessing.
- An organization with no `Subscription` row at all reports
  `Status = "NoSubscription"`, `IsActive = false` — never silently full access.

### Example enforcement wired in this increment

`PropertyEndpoints.CreatePropertyAsync` checks `IEntitlementService` before creating
a property: inactive subscription → `402 Payment Required`; at the plan's
`MaxProperties` limit → `403 Forbidden`. This is the one concrete example proving
the mechanism end-to-end; other modules (`users.max`, `apartment.rentals` gating the
apartment endpoints, `reporting.advanced`, branding entitlements) are **not** wired
yet and are the natural next increments.

## Deferred — On-Premise signed licensing (CLAUDE.md 21.3)

**Not implemented.** `EntitlementService` currently grants every On-Premise
deployment the full capability set unconditionally, regardless of
`DeploymentOptions.LicenseValidationEnabled`. This is a deliberate, explicit
placeholder, not a design decision to skip licensing:

- CLAUDE.md is explicit that fake license validation must never be implemented.
  Writing a validator that doesn't actually verify a signature would be exactly
  that, just with a more convincing name.
- Implementing it properly requires: a chosen asymmetric signing scheme, a private
  key kept outside this repository, a public-key verifier, and defined activation /
  renewal / expiration / revocation / grace-period behavior — a separate increment
  with its own review, not something to bolt onto this one.
- `EntitlementService`'s `Status` field reports `"LicenseValidationNotImplemented"`
  when `LicenseValidationEnabled = true` is set, specifically so this gap is visible
  in the API response rather than silently assumed to be enforced.

**Before any commercial On-Premise release that relies on license-gated features,
this must be implemented and this ADR updated.**

## Known gap this increment surfaced (not fixed here — out of scope)

This repository has **no EF Core migrations** for any module, and no
`Database.Migrate()` / `Database.EnsureCreated()` call anywhere in `Program.cs`.
The InMemory provider used by tests creates its schema implicitly on first access;
PostgreSQL does not. In practice this means `docker-compose up` Postgres currently
has no schema created by the application at all. `SubscriptionPlanSeeder` (and
every other `DbContext` usage) will throw against a real, empty PostgreSQL database
until this is addressed. This is a pre-existing Phase 1 gap, not something
introduced by Phase 7; fixing it is a recommended near-term increment of its own.

## Consequences

- Business modules gain one call (`IEntitlementService.GetEffectiveEntitlementsAsync`
  / `HasCapabilityAsync`) to add server-side feature gating; none are forced to do so
  by this change.
- `OrganizationEndpoints.CreateOrganizationAsync` and both `AuthEndpoints`
  org-creation paths (`RegisterAsync`, `QuickDemoAsync`) now call the shared
  `SubscriptionProvisioning.StartTrialForNewOrganizationAsync` helper so every
  "create organization" path provisions identically instead of drifting.
- `Permissions.SubscriptionsView` / `Permissions.SubscriptionsManage` were added;
  Owner gets both, Admin gets view only, Manager/Staff/Viewer get neither.
