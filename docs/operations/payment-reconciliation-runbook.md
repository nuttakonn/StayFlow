# Payment Reconciliation Issue Runbook

## Purpose
Steps to investigate and resolve a discrepancy between the folio balance shown in StayFlow PMS and the actual cash/bank records at the front desk.

## Prevention
StayFlow uses immutable financial records — payments and charges are never edited or deleted, only reversed. This creates a complete audit trail. Run daily reconciliation to catch discrepancies early.

## Common discrepancy scenarios

### 1. Payment recorded in PMS but not received (over-credit)
**Symptom:** Folio shows balance = 0 but the actual cash/bank received is less than the total.

**Investigation:**
```
GET /api/v1/billing/folios/{folioId}
```
Review each payment entry. Look for:
- Duplicate payment records (same amount, same timestamp — idempotency guard should prevent this)
- A payment entered with the wrong method (e.g., entered as `Cash` but was actually pending bank transfer)

**Resolution:**
- Create a reversal entry for the incorrect payment: use the folio adjustment endpoint
- Record the correction in the audit log with a reason
- Never silently delete or edit the original payment record

### 2. Payment received but not in PMS (under-credit)
**Symptom:** Cash register or bank slip shows a payment that is not in the folio.

**Investigation:**
Check the audit log for the reservation and folio at the time the payment should have been recorded:
```
GET /api/v1/audit?entityType=Payment&entityId=<reservationId>
```
Also check if the payment was posted to a different folio (e.g., wrong reservation number was entered).

**Resolution:**
- If the payment was simply not entered, record it now with the correct payment date and method
- If posted to the wrong folio, reverse from the wrong folio and add to the correct one
- Document the correction with the original receipt number

### 3. Folio total does not match the rate plan
**Symptom:** The folio shows a total that differs from the nightly rate × nights calculation.

**Investigation:**
- Check the reservation's `QuotedRate` — this is the rate locked at booking time and must not change
- Look for extra charges (service items, minibar, meals) posted to the folio
- Verify whether a coupon discount was applied at booking

**Resolution:**
If the rate is genuinely wrong (e.g., staff entered wrong rate at walk-in):
- Create a folio adjustment charge (negative) to correct the amount
- Note the reason in the charge description
- Alert the property manager

### 4. Nightly reconciliation steps
Run at the end of each day:

1. Export the daily transaction report: `GET /api/v1/reports/daily-transactions?date=YYYY-MM-DD`
2. Total all payments by method (Cash, Bank Transfer, Card, QR)
3. Compare against:
   - Cash drawer count
   - Bank transfer receipts
   - Card terminal Z-report
   - QR payment platform settlement report
4. Any difference > ฿0 must be investigated before close

## Escalation
- Discrepancy > ฿1,000 or any unresolved discrepancy at end of day → escalate to property manager
- Evidence of duplicate charges or unauthorized payment reversals → treat as a P1 incident and preserve logs

## Post-incident
- [ ] Record the discrepancy amount, root cause, and resolution
- [ ] Update staff training if the cause was a process error
