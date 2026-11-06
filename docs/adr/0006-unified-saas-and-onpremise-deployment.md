# ADR 0006: Unified SaaS and On-Premise Single Codebase Architecture

## Status
Accepted

## Context
StayFlow PMS must be sold both as a vendor-hosted multi-tenant SaaS and as a customer-managed, dedicated On-premise deployment. Forking the codebase into separate branches or repositories would quickly lead to diverging features, duplicate maintenance costs, broken migrations, and test drift.

## Decision
1. **Single Unified Codebase:** All features, business logic, entities, and UI components are identical between SaaS and On-premise.
2. **Infrastructure Abstractions:**
   - Configuration toggle: `DeploymentMode` (`SaaS` or `OnPremise`).
   - Deployment-specific behavior (e.g., license validation vs SaaS subscription billing, local file storage vs S3 blob storage) is isolated behind strongly-typed interfaces (`ILicenseService`, `IStorageService`).
3. **Domain Purity:** Domain entities, aggregates, and business validation must **never** contain `if (DeploymentMode == SaaS)` statements.
4. **On-Premise Licensing:** Uses asymmetric cryptographic signatures (public-key verification in app, private key held securely in vendor build system).
5. **Branding Compatibility:** Both modes utilize the 3-tier white-label branding resolver (`Property Override` $\succ$ `Organization Branding` $\succ$ `Platform Default`).

## Consequences
### Positive
- Zero code branching; all automated tests and bug fixes benefit both SaaS and On-premise.
- Straightforward CI/CD packaging (Docker image can run either mode based on environment variables).

### Negative / Trade-offs
- Requires discipline to keep infrastructure interfaces cleanly separated and avoid leaking cloud-specific SDKs into the core domain layer.
