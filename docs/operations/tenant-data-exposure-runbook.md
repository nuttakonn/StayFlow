# Suspected Tenant Data Exposure Runbook

## What this covers
A situation where there is evidence or suspicion that one customer's (organization's) data was visible to, or accessible by, another customer or an unauthorized party.

## Severity
This is a **P0 incident**. Begin immediately.

## Immediate actions (first 30 minutes)

### 1. Preserve evidence — do not alter logs
```bash
# Archive the current application logs immediately
journalctl -u stayflow-api --since "2 hours ago" > /tmp/incident-$(date +%Y%m%d%H%M%S).log
# Archive the PostgreSQL logs
cp /var/log/postgresql/postgresql-*.log /tmp/
```

### 2. Identify the scope
Determine:
- Which organization(s) may have been exposed?
- Which organization(s) may have seen data they should not have?
- What data was involved? (reservations, guest PII, financial records, branding, etc.)
- How was it accessed? (API endpoint, report export, cached page, etc.)
- Is the exposure still ongoing?

### 3. If exposure is ongoing — isolate the affected endpoint or account
```bash
# Option A: take the system offline temporarily
sudo systemctl stop stayflow-api

# Option B: if a specific user account caused the breach, disable it in the DB
psql -U stayflow_user stayflow_db -c \
  "UPDATE \"users\" SET \"is_active\" = false WHERE \"id\" = '<user-id>';"
```

## Investigation

### Check the audit log for the suspected organization
```
GET /api/v1/audit?organizationId=<org-id>&from=<start>&to=<end>
```

### Check application logs for cross-tenant queries
Search logs for the suspected organization ID appearing in requests authenticated with tokens from a different organization.

### Review EF Core query filters
The `StayFlowDbContext` applies a global query filter using `OrganizationId` from `ITenantContext`. Confirm:
- The `TenantContextMiddleware` is running before all authenticated endpoints
- No endpoint calls `IgnoreQueryFilters()` without an explicit `PropertyId` or `OrganizationId` filter in the same query
- Background jobs carry explicit `OrganizationId` scope

### Common root causes to check
1. `IgnoreQueryFilters()` used in a scope where the tenant context was null
2. A route parameter (`organizationId`, `propertyId`) accepted from the client and used as authorization evidence without verifying the authenticated user belongs to that organization
3. A cached response served to the wrong user after an organization switch

## Notification

If PII (guest names, passport numbers, national IDs, payment references) was exposed to another customer:
- Notify the affected customers immediately
- Assess whether the exposure triggers a legal or regulatory notification obligation (PDPA in Thailand, GDPR if EU guests are involved)
- Do not guess at the scope — only report what can be confirmed from logs

## Post-incident
- [ ] Write an incident timeline with log evidence
- [ ] Identify the root cause in code
- [ ] Deploy a fix and regression test
- [ ] Add an automated test that proves cross-tenant access is denied for the specific endpoint that failed
- [ ] Review all other endpoints with the same code pattern
- [ ] Document lessons learned
