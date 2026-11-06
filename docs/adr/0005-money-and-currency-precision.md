# ADR 0005: Money and Currency Precision Strategy

## Status
Accepted

## Context
Financial inaccuracies, rounding drift, and floating-point errors (e.g. `0.1 + 0.2 = 0.30000000000000004`) cannot be tolerated in billing, folios, tax calculations, and cashier reports.

## Decision
1. **Decimal Precision:** All monetary amounts (`Amount`, `Rate`, `TaxAmount`, `Deposit`, `Balance`) must use `decimal` in C# and `numeric(18,4)` or `numeric(18,2)` in PostgreSQL.
2. **Explicit Currency:** Every financial transaction, folio, and payment record must carry an explicit ISO 4217 currency code (e.g., `THB`, `USD`, `EUR`). The default currency is configured per `Property`.
3. **Immutable Ledger Entries:**
   - Posted charges, payments, and invoice line items are append-only.
   - Mistakes are never fixed by `UPDATE` or `DELETE` on posted transactions; they must be corrected via explicit compensating reversal or adjustment entries (`ReversalPayment`, `CreditAdjustment`).
4. **Rounding Rules:** Standard Banker's Rounding (`MidpointRounding.ToEven`) or explicit Thai tax rounding rules are applied only at final settlement stages, not during intermediate compounding.

## Consequences
### Positive
- Strict auditability and financial consistency.
- Reconciles 1:1 with external payment records and accounting reports.
- Prevents floating-point truncation bugs.

### Negative / Trade-offs
- Requires developers to strictly use `decimal` and avoid casting to `double` or `float` anywhere in calculation or API serialization.
