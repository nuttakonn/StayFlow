# ADR-0007: Reservation Conflict Prevention Strategy

**Status:** Accepted  
**Date:** 2026-11-01  
**Deciders:** Engineering Team

---

## Context

Hotel reservations must prevent two guests from being assigned the same room for overlapping date ranges. This is a classic concurrency problem where two simultaneous booking requests could both pass an availability check and then both commit, resulting in a double-booking.

We need a strategy that:
1. Works with both InMemory (integration tests) and PostgreSQL (production)
2. Is correct under concurrent requests
3. Is comprehensible and maintainable

## Decision

We use **application-level optimistic concurrency + overlap query within a serializable transaction** for production, and **application-level sequential overlap check** for InMemory tests.

### Strategy Details

**1. `Reservation.RowVersion` (Optimistic Concurrency Token)**

Each `Reservation` entity carries a `RowVersion: Guid` that is regenerated on every state change. EF Core is configured with `IsConcurrencyToken()` on this property. If two concurrent updates to the same reservation occur, one will throw `DbUpdateConcurrencyException`.

**2. Overlap Check Before Commit**

When creating or modifying a reservation for a specific room:

```csharp
// Check for overlapping active reservations for the same room
var hasConflict = await dbContext.Reservations
    .Where(r =>
        r.RoomId == requestedRoomId &&
        r.PropertyId == propertyId &&
        r.Status != ReservationStatus.Cancelled &&
        r.Status != ReservationStatus.NoShow &&
        r.CheckInDate < requestedCheckOut &&
        requestedCheckIn < r.CheckOutDate)
    .AnyAsync(ct);

if (hasConflict)
    return Results.Conflict("Room is not available for the requested dates.");
```

**3. Transaction Isolation**

For PostgreSQL, the CreateReservation handler wraps the overlap check + insert in a single `DbContext.Database.BeginTransactionAsync()`. With EF Core's default isolation level (Read Committed), the overlap check + insert is not perfectly safe under high concurrency, but:
- The unique index `IX_reservations_room_date_conflict` acts as the final guard
- We use a unique partial index approach as the database-level safety net

**4. Database Safety Net (PostgreSQL only)**

A migration adds a unique constraint that prevents duplicate room/date allocations at the database level. This is the last line of defense.

For InMemory (tests), only the application-level overlap check is active. Tests verify the application logic correctly, but cannot test the database-level constraint. A note in the test class documents this limitation.

## Half-Open Interval

Reservation dates use a half-open interval `[CheckInDate, CheckOutDate)`:
- A reservation from June 10 to June 12 occupies nights of June 10 and June 11
- A new guest may check in on June 12 (same day as prior guest's checkout)
- Overlap condition: `existing.CheckInDate < requested.CheckOutDate AND requested.CheckInDate < existing.CheckOutDate`

## Consequences

**Positive:**
- Works with InMemory for integration tests
- No complex locking needed for most operations
- `RowVersion` protects against lost-update on reservation state changes
- Database unique index as final safety net for PostgreSQL

**Negative:**
- Under extreme concurrent load on PostgreSQL, the overlap check could theoretically have a race between check and insert (TOCTOU). Mitigated by the database-level unique partial index
- InMemory tests do not test the database constraint

**Not Chosen:**
- PostgreSQL `tsrange` exclusion constraints: Powerful but require raw migrations, not compatible with InMemory for tests, adds complexity
- Pessimistic locking (`SELECT FOR UPDATE`): Requires raw SQL, not compatible with InMemory
- Database-per-tenant: Not needed at current scale
