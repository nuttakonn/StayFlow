# ADR 0004: Date, Time, and Timezone Handling Strategy

## Status
Accepted

## Context
Hospitality systems deal heavily with dates and times across differing contexts:
- Audit records, payment processing timestamps, created/updated timestamps must be unambiguous across the globe.
- Hotel business dates (check-in, check-out, night audit) depend strictly on the **local time of the physical property**, not the server's local clock or the guest's mobile phone timezone.
- A stay from June 10 to June 12 represents two nights (June 10 and June 11); check-out occurs on June 12 at property check-out time (e.g. 12:00 PM Bangkok Time).

## Decision
1. **Timestamp Storage:** All point-in-time timestamps (`CreatedAt`, `UpdatedAt`, `PaymentProcessedAt`, `AuditEventTimestamp`) are stored as **UTC** (`timestamp with time zone` / `DateTimeOffset` in C#).
2. **Property Timezone:** Every `Property` entity has an explicit IANA Timezone ID (e.g., `Asia/Bangkok`, `UTC`, `America/New_York`).
3. **Business Dates:**
   - Hotel stays and room availability use half-open date intervals: `[CheckInDate, CheckOutDate)` represented as `DateOnly` in .NET (or date types in PostgreSQL).
   - Local business operations (e.g. daily manager reports, night audit date rollover) are calculated relative to the property's configured IANA timezone.
4. **Server Clock Independence:** Application code must never use `DateTime.Now` without specifying UTC or converting to the property's timezone. Always use `DateTimeOffset.UtcNow` or a time provider abstraction (`TimeProvider`).

## Consequences
### Positive
- Prevents daylight-saving time anomalies and server relocation bugs.
- Consistent availability interval arithmetic: two stays overlap if and only if `existing.Start < requested.End && requested.Start < existing.End`.
- Frontend displays formatted local time with explicit timezone labels.

### Negative / Trade-offs
- Developers must remember to use `DateOnly` for reservation stay dates and `DateTimeOffset.UtcNow` for event timestamps.
