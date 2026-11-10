# StayFlow PMS --- Product & Engineering Implementation Plan

> **Purpose:** Build a production-ready Property Management System (PMS)
> for hotels and apartments, delivered as an on-premise installation on
> the customer's own server, with the same codebase capable of SaaS
> deployment in future. Includes a channel manager integration layer for
> connecting to OTAs (Agoda, Booking.com, Traveloka, Trip.com) through
> third-party channel managers (STAAH, SiteMinder).
>
> **Status:** Core hotel & apartment modules implemented. EF Core migrations complete. Channel manager, rate management, ancillary services, Thai compliance (M11), email automation (M12), direct booking engine (M13), and security hardening (M14) complete. Ready for UAT and pilot hotel go-live.\
> **Primary instruction file:** `Claude.md`\
> **Important:** Production-ready is an engineering target, not a claim
> that the system is safe to launch without completing the release
> gates, security review, operational setup, and user acceptance testing
> in this document.

------------------------------------------------------------------------

# 1. Product vision and scope

## 1.1 Product vision

StayFlow PMS is a unified Property Management System designed for
hotels and apartments.

The platform supports two deployment models:

- **On-premise (primary):** Installed on the customer's own server at
  their hotel or apartment building. The customer owns their data,
  manages their own backups, and pays a one-time or annual license fee.
  This is the primary commercial model for the first release.
- **SaaS (future):** A vendor-hosted multi-tenant platform. The same
  codebase supports this via DeploymentMode configuration. Do not build
  SaaS-specific infrastructure until there is a confirmed commercial
  need.

The product uses one codebase, one domain architecture, and shared
business foundations. On-premise and SaaS must not become separate
code forks.

**Language support:** The back-office UI supports Thai, English, and
(as a tier-2 addition) Chinese Simplified. Language is a per-user
preference stored on the user profile; each staff member chooses their
own language independently of the property's locale setting. Thai
compliance documents (R.S.3, R.S.4, TM.30) are always rendered in
Thai regardless of the user's language preference.

Each customer organization can manage one or more properties,
including hotels, apartments, or a combination of both.

The first release focuses on the shared foundation:

-   Organization and property setup
-   Buildings, floors, room types, and rooms
-   Room status and operational status
-   Availability calendar (including drag-and-drop management)
-   Rate plans: weekday, weekend, seasonal, minimum stay, and promotional rates
-   Promotions and discount codes
-   Reservation lifecycle
-   Group bookings
-   Guest / tenant profiles
-   Check-in and check-out
-   Basic charges, payments, and folios
-   Quotations, invoices, and tax documents
-   Ancillary services and meal plan packages
-   Staff accounts, roles, and permissions
-   Audit history and operational reporting
-   Excel / CSV export for all key reports

The **Direct Booking Engine** (a customer-facing web booking page) is planned as a separate milestone after the back-office PMS is stable. It reuses the same availability and rate engine.

The system must support hotel and apartment workflows without forcing
both business types into identical rules.

## 1.2 Target business types

### Hotel

Typical workflow:

1.  Configure room types, rates, and rooms.
2.  Receive a reservation or create a walk-in booking.
3.  Check availability for the requested dates.
4.  Assign a room, or leave the reservation unassigned until arrival.
5.  Check the guest in.
6.  Track room charges and additional charges.
7.  Check the guest out and close the folio.
8.  Clean and inspect the room before returning it to sellable
    inventory.

Hotel concepts include nightly stays, variable rates, occupancy, guest
count, housekeeping, room moves, early check-in, late check-out, short
stays, rate plans, promotional codes, group bookings, and ancillary
charges (meals, services).

### Apartment

Typical workflow:

1.  Configure buildings, floors, room types, and rental rooms.
2.  Register a tenant.
3.  Create a rental agreement with start and end dates.
4.  Assign a room to the tenant.
5.  Track occupancy and room status.
6.  Generate recurring rent and utility charges.
7.  Record payments and outstanding balances.
8.  Handle move-out, deposit settlement, and room turnover.

Apartment concepts include monthly rental periods, agreements, deposits,
utility meter readings, recurring invoices, overdue balances, and
move-out settlement.

**Scope decision:** The first implementation should deliver shared room
and reservation foundations. Apartment-specific recurring billing and
hotel-specific advanced revenue/channel features are separate later
modules. Do not pretend a nightly hotel reservation is the same entity
or lifecycle as a long-term rental agreement.

## 1.3 Out of scope for the first release

Do not implement these until the core PMS is stable:

-   Direct OTA API integrations (Agoda YCS, Booking.com Connectivity,
    Expedia EQC) — connect via a channel manager instead
-   **Dynamic / algorithmic pricing and revenue optimization** — manual rate
    rules (weekday/weekend/seasonal) are in scope; AI-driven demand pricing is not
-   Native iOS / Android apps
-   Point of Sale (POS) system for restaurant / spa — ancillary room
    charges are in scope; a full POS terminal is not
-   Digital / contactless check-in (kiosk, mobile link) — standard
    front-desk check-in is in scope
-   Accounting / ERP integrations
-   Payroll
-   Advanced housekeeping workforce optimization
-   AI pricing or AI customer support
-   Multi-country tax and localization rules — Thai tax compliance is
    in scope; multi-jurisdictional rules are not
-   Franchise / chain-level complex revenue consolidation
-   SaaS subscription billing and tenant provisioning infrastructure
-   Competitor rate shopping / rate intelligence dashboards

Keep future integration points in mind, but do not build speculative
abstractions.

## 1.4 Channel manager integration (planned — M9)

Hotels sell rooms through Online Travel Agencies (OTAs). The PMS must
connect to OTAs through a third-party channel manager, not by
implementing direct OTA APIs.

Supported integration model:

```
StayFlow PMS  ←→  Channel Manager  ←→  Agoda
                                   ←→  Booking.com
                                   ←→  Traveloka
                                   ←→  Trip.com
                                   ←→  Expedia
```

Target channel manager providers (in priority order):

1. **STAAH** — strong Asia-Pacific coverage, common in Thailand
2. **SiteMinder** — largest global coverage, 450+ OTAs

Integration responsibilities:

- **Outbound (PMS → Channel Manager):** Push room availability and
  rate changes when rooms are booked, cancelled, or status changes.
- **Inbound (Channel Manager → PMS):** Receive new bookings via
  webhook and create reservations automatically.
- **Source tracking:** Every reservation records its source
  (Direct, Agoda, BookingCom, Traveloka, TripCom, Expedia, etc.).
- **Conflict prevention:** Inbound OTA bookings must go through the
  same availability and concurrency checks as direct bookings.
- **Rate mapping:** Map PMS room types and rates to channel manager
  rate plans.

Do not build channel manager features until the core hotel PMS
(M0–M6) is stable and tested with at least one pilot hotel.

------------------------------------------------------------------------

# 2. Product principles and non-negotiable rules

1.  **Tenant isolation:** A customer must never access another
    customer's data.
2.  **Property scope:** Users may have access to all properties in an
    organization or only selected properties.
3.  **Server-side authorization:** Hiding a button in the UI is not
    authorization.
4.  **Correct inventory:** Prevent overlapping active room allocations
    for the same room and stay period.
5.  **Transactional writes:** Booking, room assignment, check-in,
    check-out, payment, and invoice operations must be atomic where
    required.
6.  **Auditability:** Important business changes must record actor,
    timestamp, entity, action, and relevant before/after values.
7.  **Money safety:** Use decimal types and explicit currency. Never use
    floating-point types for monetary values.
8.  **Time safety:** Store timestamps in UTC; preserve each property's
    IANA time zone for business-date calculations and display.
9.  **Explicit business rules:** Avoid silently assuming hotel and
    apartment policies are identical.
10. **No fake production readiness:** Do not leave placeholder security,
    fake integrations, unimplemented critical paths, or untested
    migrations while describing the system as production-ready.
11. **Backward-compatible evolution:** Use database migrations and
    deliberate API versioning / compatibility practices.
12. **Operational visibility:** Logs, metrics, traces, health checks,
    backups, and restore procedures are part of the product.

------------------------------------------------------------------------

# 3. Recommended architecture

## 3.1 Technology baseline

Use the following unless repository inspection reveals an existing
justified standard:

  -----------------------------------------------------------------------
  Area                                Default
  ----------------------------------- -----------------------------------
  Backend                             ASP.NET Core 10 Web API

  Language                            C# with nullable reference types

  Architecture                        Modular Monolith

  Application patterns                Use cases / CQRS; MediatR only
                                      where it adds value

  Persistence                         EF Core + PostgreSQL

  Database access                     EF Core by default; use SQL/Dapper
                                      only for measured query needs

  Frontend                            React + TypeScript

  Internationalisation                react-i18next; locale JSON files
                                      per language; language stored on
                                      user profile; date/number/currency
                                      via Intl API driven by user locale

  Build tooling                       Vite

  Styling                             Tailwind CSS + shadcn/ui

  Client routing                      React Router

  Server state                        TanStack Query

  Forms                               React Hook Form + Zod

  API contract                        OpenAPI; generated or
                                      contract-checked TypeScript client

  Authentication                      ASP.NET Core Identity or a
                                      well-supported OIDC provider

  Authorization                       Role + permission +
                                      organization/property scope

  Cache                               Redis only for demonstrated needs;
                                      PostgreSQL remains source of truth

  Tests                               xUnit, integration tests,
                                      Playwright

  Containers                          Docker / Docker Compose for local
                                      development

  CI/CD                               GitHub Actions or existing
                                      repository CI

  Observability                       Structured logging, OpenTelemetry,
                                      metrics, traces

  Secrets                             Environment / deployment secret
                                      manager; never committed
  -----------------------------------------------------------------------

Do not add infrastructure or libraries solely because they are listed
here. Confirm current compatibility and maintenance status before
adoption.

## 3.2 Modular monolith boundaries

Organize the backend around business modules:

-   Identity & Access
-   Tenancy & Organizations
-   Property Configuration
-   Inventory & Rooms
-   Rate Management (rate plans, pricing rules, promotions, coupons)
-   Guests & Tenants
-   Reservations (including group bookings and booking holds)
-   Stay Operations
-   Billing & Folios (charges, ancillary services, packages)
-   Payments
-   Documents (quotations, invoices, tax invoices, receipts)
-   Housekeeping
-   Maintenance
-   Reporting & Analytics (ADR, RevPAR, occupancy, exports)
-   Notifications (email automation, daily reports)
-   Booking Engine (direct web booking — future milestone)
-   Thai Compliance (R.S.3, R.S.4, TM.30 registration)
-   Audit

Each module owns its business rules and data access. Avoid a single
giant service, generic repository layer, or cross-module database access
that bypasses module contracts.

A module may expose application interfaces / events to other modules.
Keep module dependencies explicit and preferably one-directional.

## 3.3 Suggested repository structure

``` text
stayflow/
├── Claude.md
├── README.md
├── docs/
│   ├── product/
│   ├── architecture/
│   ├── adr/
│   ├── api/
│   ├── security/
│   ├── operations/
│   └── runbooks/
├── src/
│   ├── backend/
│   │   ├── StayFlow.Api/
│   │   ├── StayFlow.BuildingBlocks/
│   │   └── Modules/
│   │       ├── Identity/
│   │       ├── Tenancy/
│   │       ├── Properties/
│   │       ├── Inventory/
│   │       ├── Parties/
│   │       ├── Reservations/
│   │       ├── Operations/
│   │       ├── Billing/
│   │       ├── Payments/
│   │       ├── Housekeeping/
│   │       ├── Maintenance/
│   │       ├── Reporting/
│   │       └── Audit/
│   └── frontend/
│       └── stayflow-web/
├── tests/
│   ├── backend/
│   │   ├── UnitTests/
│   │   ├── IntegrationTests/
│   │   └── ArchitectureTests/
│   └── frontend/
│       ├── unit/
│       └── e2e/
├── deploy/
│   ├── docker/
│   └── ci/
└── scripts/
```

Adapt names and project boundaries to the repository. Do not create
empty projects for every future module on day one.

## 3.4 Multi-tenancy design

Use a shared database and shared schema initially, with explicit
`OrganizationId` on every organization-owned aggregate / record.

Rules:

-   Every request must resolve an authenticated user and active
    organization context.
-   Validate organization membership on the server for every operation.
-   Apply organization scope in the data access boundary; do not rely on
    developers remembering to add a `Where` clause in every handler.
-   Include `PropertyId` where data belongs to a specific property.
-   Validate that a property belongs to the active organization.
-   Never trust `OrganizationId`, `PropertyId`, role, or ownership
    identifiers supplied by the browser without checking authorization.
-   Add database constraints and indexes that support tenant-scoped
    queries.
-   Consider PostgreSQL row-level security as defense in depth only if
    connection/session context can be set and reset safely with pooling.
    Application authorization remains mandatory.
-   Background jobs must carry an explicit organization context and must
    not accidentally process records across tenants.
-   Audit and test cross-tenant access attempts.

Deployment-specific tenancy rules:

- SaaS uses shared application infrastructure with strict
  organization-level data isolation.
- On-premise may run as a dedicated single-customer installation.
- Both deployment models must preserve OrganizationId ownership
  and the same authorization model.
- Deployment mode must be controlled through validated configuration.
- Business domain logic must not contain scattered deployment-mode
  conditionals.
- Customer-specific deployment requirements must be handled through
  configuration and explicit infrastructure boundaries.

Do not introduce database-per-tenant until there is a demonstrated
isolation, regulatory, or scale requirement.

------------------------------------------------------------------------

# 4. Domain model and key business rules

## 4.1 Organization and property

Core entities:

-   Organization
-   OrganizationMembership
-   User
-   Role
-   Permission
-   Property
-   PropertySettings
-   Building
-   Floor

A property should have:

-   Business type: `Hotel` or `Apartment`
-   Name and address
-   IANA time zone
-   Default currency
-   Business settings
-   Contact details
-   Active / inactive status

A user profile should have:

-   PreferredLanguage: `th` | `en` | `zh-CN` (default: `en`)
-   The language preference is stored on the user record and applied
    per-session; it is independent of the property's locale.
-   Supported tier-1 languages: Thai (`th`), English (`en`).
-   Tier-2 (add when a customer requires it): Chinese Simplified (`zh-CN`).
-   The backend returns validation and error messages in the user's
    language via the `Accept-Language` header or the stored preference.

Do not assume one organization has only one property.

## 4.2 Room inventory

Core entities:

-   RoomType
-   Room
-   RoomFeature (optional)
-   RoomStatusHistory

Suggested room fields:

-   Id
-   OrganizationId
-   PropertyId
-   BuildingId (optional)
-   FloorId (optional)
-   RoomTypeId
-   RoomNumber / Code
-   DisplayName (optional)
-   Capacity
-   OperationalStatus
-   IsActive
-   Notes

Separate **operational status** from **occupancy / availability**.

Example operational statuses:

-   `Clean`
-   `Dirty`
-   `Cleaning`
-   `Inspected`
-   `OutOfService`
-   `Maintenance`

Occupancy is derived from active stays / allocations and date range, not
manually set as the only source of truth.

Do not use one status field to represent both "occupied" and "dirty"; a
room can be occupied and have other operational attributes.

## 4.3 Reservations and rental agreements

Avoid one overloaded table with dozens of nullable fields for unrelated
hotel and apartment concepts.

Shared concepts may include:

-   Customer / Party
-   Property
-   Room type or requested room
-   Date / period
-   Status
-   Price quote
-   Allocation
-   Notes
-   Source / reference

Hotel reservation:

-   Check-in date/time policy
-   Check-out date/time policy
-   Number of nights
-   Adults / children / occupants
-   Room type
-   Optional room assignment
-   Rate plan / quoted rate
-   Reservation status
-   Cancellation policy and cancellation record

Apartment rental agreement:

-   Tenant
-   Room
-   Lease start / end
-   Monthly rent
-   Deposit
-   Billing day
-   Agreement status
-   Move-in / move-out details

Apartment agreements should not be forced into hotel night-based
inventory logic.

## 4.4 Date-range overlap

For hotel stays, model occupied dates as a half-open interval:

`[CheckInDate, CheckOutDate)`

A reservation from 10 June to 12 June occupies the nights of 10 and 11
June; a new guest may check in on 12 June if turnover policy permits.

Two ranges overlap when:

`existing.Start < requested.End AND requested.Start < existing.End`

For apartment room assignments, define whether agreements use inclusive
calendar dates or half-open periods. Document the policy and enforce it
consistently.

Prevent double allocation at the database / transaction level, not only
with a frontend availability check. Investigate PostgreSQL exclusion
constraints for date-range conflicts, or use a robust transaction and
locking strategy. Document the selected approach and test concurrent
booking attempts.

## 4.5 Reservation lifecycle

Define explicit, validated transitions. Example hotel lifecycle:

-   `Pending`
-   `Confirmed`
-   `CheckedIn`
-   `CheckedOut`
-   `Cancelled`
-   `NoShow`

Do not permit arbitrary status updates. Use commands such as
ConfirmReservation, AssignRoom, CheckIn, CheckOut, CancelReservation,
and MarkNoShow.

Each command must validate current state, actor permission, date rules,
room availability, and required data.

## 4.6 Billing and payments

For the first release, support basic folios and payment recording:

-   Folio
-   FolioItem / Charge
-   Invoice (if legally / operationally required)
-   Payment
-   PaymentAllocation
-   Refund / reversal record

Use immutable financial transaction history. Correct mistakes with
reversal / adjustment entries rather than silently editing a completed
payment.

Payment methods may include cash, bank transfer, card, and QR payment as
manually recorded methods. Do not claim to process online payments until
a payment provider is integrated and verified.

Tax invoices, withholding tax, VAT, receipt numbering, and Thai
accounting compliance require a separate requirements and
legal/accounting review before production use.

## 4.7 Rate management

Core entities:

-   RatePlan
-   RateRule (weekday, weekend, long holiday, weekly, monthly)
-   RatePlanRestriction (minimum stay, advance booking window, blackout dates)
-   RatePlanVisibility (public, staff-only / agent rate — hidden from booking engine)

Rate plan fields:

-   Id
-   OrganizationId
-   PropertyId
-   RoomTypeId (nullable — plan may apply to all room types)
-   Name / Code
-   BaseRate (decimal)
-   Currency
-   RateType (PerNight, Weekly, Monthly)
-   IsPublic (false = staff/agent only, hidden from guest-facing booking)
-   Rules (list of RateRule overrides by day type or date range)
-   Restrictions (min nights, max advance days, blackout dates)
-   IsActive

Business rules:

-   A reservation always records the quoted rate and the rate plan used
    at time of booking; never recalculate silently if the plan changes.
-   Staff-only rates must never be accessible from the public booking
    engine endpoint.
-   Rate plan changes do not retroactively change committed reservations.

## 4.8 Promotions and coupons

Core entities:

-   Promotion
-   PromotionRule (discount type, eligible room types, eligible dates)
-   Coupon

Supported promotion types:

-   Percentage or fixed discount off the standard rate
-   Early bird (book X days in advance)
-   Last minute (book within X days of arrival)
-   Long stay (minimum N nights)
-   Free night (stay N nights, pay N-1)
-   Room-only (exclude meals / packages)
-   Blackout dates where no discount applies
-   Restricted to specific room types or stay periods

Coupon fields:

-   Code (unique, case-insensitive)
-   DiscountType (percentage, fixed amount, free night)
-   Value
-   MaxUses (nullable = unlimited)
-   UsedCount
-   ValidFrom / ValidTo
-   EligibleRoomTypes (nullable = all)
-   IsActive

Business rules:

-   Validate coupons server-side; never trust client-side discount
    calculation.
-   Record the coupon code and discount amount on the reservation at
    time of booking.
-   Multiple promotions cannot stack unless explicitly configured.

## 4.9 Group bookings

A group booking is a logical container for multiple reservations under
one coordinator.

-   GroupBooking (coordinator name, company, agreed rate, notes)
-   Each room is a separate Reservation linked to the GroupBooking
-   Shared folio option (all charges to one invoice) or split folios
-   Group cancellation policy may differ from individual policy

Business rules:

-   Group pricing is set manually by staff; it does not need to follow
    public rate plans.
-   Cancelling a group cancels linked reservations according to the
    group cancellation policy.

## 4.10 Ancillary services and packages

-   ServiceItem (name, price, unit, category: Meal / Spa / Transfer / Other)
-   Package (a named bundle of RoomRate + ServiceItems, e.g., "Breakfast Included")
-   PackageItem (links Package to ServiceItems with quantities)

Meal plan types: RoomOnly, BedAndBreakfast, HalfBoard, FullBoard.

Business rules:

-   Mandatory packages (e.g., mandatory breakfast during peak period)
    must be configurable per rate plan.
-   Package charges post automatically to the folio at check-in.
-   À-la-carte service charges may be added to the folio at any time
    before check-out.

## 4.11 Thai government compliance (Thailand market)

Thai law requires hotels to submit guest registration data to
government agencies. This is mandatory for any hotel operating in
Thailand.

Required documents:

-   **R.S.3** — Hotel guest registration card (per guest, per stay)
-   **R.S.4** — Guest registration form (summary report)
-   **TM.30** — Foreign guest report to the Immigration Bureau

Implementation requirements:

-   Collect required fields during check-in: full name, nationality,
    passport / ID number, date of birth, visa type (for foreigners),
    country of origin, arrival/departure dates.
-   Generate R.S.3 and R.S.4 documents from reservation and guest data.
-   Generate TM.30 report (foreign guests only) with correct format.
-   Export to Excel format compatible with the Immigration Bureau and
    Department of Provincial Administration (DOPA) import tools.
-   Store original submitted values as immutable records; corrections
    require an amendment record.
-   Thai citizen guests require Thai national ID; foreign guests require
    passport number.

This is a legal requirement, not an optional feature, for hotel
properties operating in Thailand. Do not launch a Thai hotel without
verified compliance.

------------------------------------------------------------------------

# 5. Product modules and implementation sequence

## Phase 0 --- Discovery and repository baseline

Deliverables:

-   Confirm target customer and initial business workflow.
-   Decide whether the first release targets hotels, apartments, or both
    with shared room operations.
-   Write user stories and acceptance criteria.
-   Inspect repository, SDKs, installed runtimes, and existing
    conventions.
-   Create architecture decision records (ADRs).
-   Establish coding standards, local development, CI, and test
    strategy.

Exit criteria:

-   A developer can clone the repository, configure local secrets, start
    dependencies, run migrations, run the application, and execute tests
    using documented commands.
-   Product scope and terminology are documented.

## Phase 1 --- Platform foundation

Implement:

-   Backend and frontend solution structure
-   PostgreSQL connection and migrations
-   Health checks
-   Structured logging
-   Global error handling and validation
-   OpenAPI
-   Authentication
-   Organization creation / onboarding
-   Organization membership
-   Property creation and settings
-   Role / permission foundation
-   Tenant-scoped data access
-   Audit foundation
-   Docker Compose for local dependencies
-   CI build and test pipeline

Acceptance criteria:

-   User can register / sign in through the selected authentication
    flow.
-   User can create or join an organization according to the invitation
    policy.
-   User can create a property.
-   A user cannot read or mutate another organization's data by changing
    an identifier.
-   Authorization tests cover organization and property scope.
-   Database migrations can build a clean database.

## Phase 2 --- Room and property management

Implement:

-   Property profile and business type
-   Buildings and floors
-   Room types
-   Room creation, edit, deactivate
-   Room capacity and features
-   Room operational status
-   Room status history
-   Room list with search, filter, pagination
-   Room detail
-   Bulk import only after validating format and error handling

Acceptance criteria:

-   Staff can configure a property hierarchy.
-   Room codes are unique within the agreed scope.
-   Inactive rooms cannot receive new reservations / agreements.
-   Operational status changes are permission-controlled and audited.
-   UI clearly distinguishes operational status from occupancy.

## Phase 3 --- Availability, reservations, and rate management

Implement:

-   Calendar / room availability view (drag-and-drop support for moves
    and date changes)
-   Availability query by property, room type, date range
-   Booking types: standard, temporary hold / block, walk-in, urgent
-   Hotel reservation create / update / confirm / cancel
-   Walk-in reservation
-   Group booking (multiple rooms under one group, manual pricing)
-   Room assignment and room move
-   Reservation detail and timeline
-   Conflict prevention under concurrent requests
-   Guest profile and search
-   Reservation notes and audit events
-   Change summary view: new, changed, pending-payment, and
    pending-confirmation bookings
-   Rate plan management: weekday / weekend / holiday / weekly /
    monthly rates, minimum stay rules, blackout dates
-   Staff-only / agent rate plans (hidden from public)
-   Special-day surcharges (festivals, events)
-   Advance booking window limits per rate plan
-   Cancellation and change policies per rate plan
-   Promotions engine: early bird, last minute, long stay, free night,
    room-only discounts
-   Coupon / discount code creation and redemption

Acceptance criteria:

-   Availability reflects reservations, active stays, and temporary holds.
-   Overlapping allocations cannot be committed for the same room.
-   Two concurrent booking requests for the same room and period cannot
    both succeed.
-   Cancelled reservations no longer block inventory according to
    documented policy.
-   Staff-only rate plans cannot be accessed from public-facing endpoints.
-   Coupon codes are validated server-side; discount amounts are recorded
    on the reservation at booking time.
-   Group bookings link correctly to individual reservations.
-   All transitions are validated on the backend.

## Phase 4 --- Stay operations

Implement:

-   Arrival / departure lists
-   Check-in
-   Check-out
-   Room move
-   Early / late check-in/out policy configuration where needed
-   Housekeeping status updates
-   Out-of-service room handling
-   Operational dashboard

Acceptance criteria:

-   Check-in requires a valid reservation or supported walk-in workflow.
-   A room cannot be checked into if it is unavailable or blocked.
-   Check-out closes the stay according to policy and updates room
    operations.
-   Every important action is audited.
-   Staff can identify arrivals, in-house guests, departures, and rooms
    needing cleaning.

## Phase 5 --- Billing, documents, and reporting

Implement:

-   Folio creation
-   Room charges
-   Ancillary / service charges
-   Package charges (auto-post on check-in for mandatory packages)
-   Additional charges
-   Payment recording (cash, bank transfer, card, QR — manually recorded)
-   Payment allocation
-   Reversal / adjustment workflow
-   Quotations (pre-booking price estimates, convertible to reservation)
-   Tax invoice generation with running receipt numbers
-   Daily transaction report
-   Occupancy report with ADR and RevPAR metrics
-   Basic revenue report
-   Excel / CSV export for all reports
-   Automated daily summary report delivered by email

Acceptance criteria:

-   Amounts use decimal precision and explicit currency.
-   Paid / outstanding balances are calculated consistently.
-   Completed financial records cannot be silently deleted or
    overwritten.
-   Reports respect organization, property, and date scope.
-   Report totals reconcile with underlying transactions.
-   ADR and RevPAR calculations match industry definitions.
-   Excel exports open correctly in Thai locale (date/number format).
-   Automated daily email is delivered reliably and respects opt-out.

## Phase 6 --- Apartment workflow

Implement separately from hotel reservations:

-   Tenant onboarding
-   Rental agreement
-   Room assignment for a rental period
-   Lease renewal / termination
-   Deposit tracking
-   Recurring rent charge generation
-   Utility meter readings
-   Utility charge calculation
-   Monthly billing
-   Outstanding balance and payment tracking
-   Move-out settlement

Acceptance criteria:

-   A room cannot have overlapping active rental agreements under the
    agreed date policy.
-   Recurring billing is idempotent: rerunning a billing job cannot
    create duplicate charges.
-   Meter readings retain historical values and reading dates.
-   Agreement changes are audited.
-   Move-out settlement reconciles rent, utilities, payments, and
    deposit adjustments.

## Phase 7 --- SaaS readiness and customer operations

Implement:

-   Subscription plan model and entitlements
-   Trial / subscription status
-   Usage limits where commercially needed
-   Billing provider integration (after provider selection)
-   Organization onboarding flow
-   Invitation flow
-   Email notifications
-   Export / data portability
-   Support diagnostics with strict tenant privacy
-   Backup and restore automation
-   Deployment environments
-   Monitoring and alerting
-   Incident and recovery runbooks

Do not block core room-management development on a payment subscription
integration. Keep entitlement checks isolated so monetization can be
added without rewriting business modules.

## Phase 8 (new) --- Thai government compliance

Implement for hotel properties operating in Thailand:

-   Guest detail fields: Thai national ID, passport number, nationality,
    date of birth, visa type, country of origin
-   R.S.3 (guest registration card) — generated per guest per stay
-   R.S.4 (guest registration summary report)
-   TM.30 (foreign guest immigration report — foreigners only)
-   Excel export formatted for DOPA and Immigration Bureau import tools
-   Immutable submitted record + amendment workflow if correction needed
-   Configuration flag: enable Thai compliance mode per property

Acceptance criteria:

-   R.S.3, R.S.4, and TM.30 contain all legally required fields.
-   Excel output passes format validation for the respective government
    system before release.
-   Amendment records are audited with original and corrected values.
-   Thai compliance fields are only required when the property is
    configured for Thai compliance mode.

Do not launch any hotel property in Thailand without Thai compliance
verification by a qualified person. This is a legal obligation.

## Phase 11 (new) --- Direct booking engine

The booking engine provides a customer-facing web page for direct
reservations, reducing OTA dependency.

Implement:

-   Embeddable or standalone booking page per property
-   Real-time availability and public rate display
-   Room type selection with photos, descriptions, and amenities
-   Optional room-number selection (toggle on/off per property policy)
-   Promotion and coupon code entry and validation
-   Payment hold timer (lock room while awaiting payment; release
    automatically if unpaid within configured window)
-   Advance booking window enforcement
-   Booking policy acknowledgement before confirmation
-   Payment method support: bank transfer (Thai banks), credit card,
    QR payment, e-wallet — where a provider is integrated
-   Auto-confirmation on payment receipt for card / e-wallet
-   Booking confirmation email to guest
-   Dorm room / shared room type support (optional, hostel use case)

Security requirements:

-   All public booking endpoints are rate-limited.
-   Coupon validation and pricing are server-side only.
-   Payment hold timer uses a server-side lock; client cannot extend it.
-   Booking engine does not expose staff-only rate plans.
-   Payment provider PCI compliance is the provider's scope; StayFlow
    records payment reference and status only.

Acceptance criteria:

-   A guest can browse availability, select dates and room type, apply
    a coupon, and complete a booking without staff intervention.
-   Double-booking prevention applies to booking engine bookings.
-   Booking engine bookings enter the same PMS reservation lifecycle.
-   Staff-only rates are never accessible from the public endpoint.
-   Payment hold timer releases the room lock correctly on timeout.
-   Confirmation email is sent with booking reference and details.

Do not implement the booking engine until the back-office PMS (M0–M7)
is stable. The booking engine reuses the existing availability engine
and rate plans — do not duplicate this logic.

------------------------------------------------------------------------

# 6. Frontend information architecture

Recommended primary navigation:

-   Overview (dashboard: ADR, RevPAR, occupancy, revenue, arrivals/departures)
-   Reservations (list, group bookings, booking holds, change summary)
-   Room Calendar (drag-and-drop availability calendar)
-   Rooms
-   Guests / Tenants
-   Check-in / Check-out
-   Housekeeping
-   Rates & Promotions (rate plans, coupons, packages, ancillary services)
-   Billing (folios, charges, payments, quotations, invoices)
-   Reports (occupancy, revenue, ADR/RevPAR, Excel export, email schedule)
-   Thai Compliance (R.S.3, R.S.4, TM.30 — hotel properties in Thailand only)
-   Channel Manager (OTA connections, rate mappings)
-   Booking Engine (settings for direct booking page — M11)
-   Property Settings
-   Users & Permissions

Show or hide business-specific navigation based on property type and
permissions. Backend authorization must still enforce access.

## 6.1 Required screens for the first usable release

1.  Sign in
2.  Organization onboarding
3.  Property selector
4.  Dashboard (occupancy rate, ADR, RevPAR, revenue today, arrivals/departures)
5.  Property settings
6.  Building / floor management
7.  Room type list and form
8.  Room list and form
9.  Room detail and status history
10. Availability calendar (drag-and-drop; booking holds; change summary panel)
11. Reservation list and detail
12. Create reservation (with rate plan and coupon selection)
13. Group booking management
14. Guest profile
15. Arrival / departure operations
16. Rate plan list and form
17. Promotions and coupon management
18. Ancillary services and packages
19. Folio and billing
20. Quotation and invoice management
21. Thai compliance reports (R.S.3, R.S.4, TM.30) — Thailand properties only
22. Reports with Excel export
23. User and role management
24. Audit log view for authorized roles

## 6.2 UX standards

-   Responsive layout for desktop, tablet, and mobile.
-   Desktop-first for front-desk and administrative workflows.
-   Accessible labels, keyboard navigation, focus states, and color
    contrast.
-   Clear loading, empty, error, and success states.
-   Confirm destructive or irreversible actions.
-   Prevent accidental double submission.
-   Use consistent date, time, number, and currency formatting based on
    property settings and the user's locale.
-   Provide a language switcher accessible from the user profile / top
    bar. Changing language takes effect immediately without requiring
    a full logout.
-   All UI strings must use i18n keys; never hardcode display text in
    components. Thai, English, and Chinese Simplified locale files must
    be kept in sync when any string changes.
-   Thai compliance documents (R.S.3, R.S.4, TM.30) are always rendered
    in Thai regardless of user language preference.
-   Use server-side pagination and filtering for large datasets.
-   Never display another tenant's data in cached client state after
    switching organizations.
-   Do not communicate room status through color alone; include text and
    accessible labels.

------------------------------------------------------------------------

# 7. API design standards

-   Use resource-oriented routes and explicit command endpoints for
    state transitions where appropriate.
-   Use DTOs; never serialize EF Core entities directly.
-   Validate all input on the server.
-   Use consistent problem-details error responses.
-   Apply pagination, filtering, and sorting with allow-listed fields.
-   Use optimistic concurrency tokens for mutable records where
    appropriate.
-   Use idempotency keys for operations that may be retried, especially
    payment and booking creation.
-   Do not expose internal exception messages or stack traces to
    clients.
-   Include correlation / trace identifiers in responses and logs.
-   Version APIs deliberately when a breaking change is unavoidable.
-   Document authorization requirements and error cases in OpenAPI.

Example route conventions:

``` text
/api/v1/organizations
/api/v1/properties
/api/v1/properties/{propertyId}/buildings
/api/v1/properties/{propertyId}/floors
/api/v1/properties/{propertyId}/room-types
/api/v1/properties/{propertyId}/rooms
/api/v1/properties/{propertyId}/availability
/api/v1/properties/{propertyId}/reservations
/api/v1/reservations/{reservationId}/confirm
/api/v1/reservations/{reservationId}/assign-room
/api/v1/reservations/{reservationId}/check-in
/api/v1/reservations/{reservationId}/check-out
```

These are conventions, not a requirement to create every endpoint in the
first sprint.

------------------------------------------------------------------------

# 8. Security requirements

Use OWASP ASVS as a security verification reference and OWASP API
Security Top 10 as an API threat checklist.

Minimum requirements:

-   Secure password handling through a maintained identity framework or
    OIDC provider.
-   MFA support for privileged accounts where supported.
-   Secure cookie / token storage strategy; do not store long-lived
    sensitive tokens in localStorage by default.
-   Short-lived access tokens and safe refresh-token rotation /
    revocation if using JWT.
-   CSRF protection when using cookies.
-   CORS allow-list; never use wildcard origins with credentials.
-   Rate limiting for authentication and sensitive endpoints.
-   Server-side validation and output encoding.
-   Parameterized database access.
-   Protection against IDOR / broken object-level authorization.
-   Tenant and property scope checks on every relevant query and
    command.
-   Secrets stored outside source control.
-   TLS in every non-local environment.
-   Secure headers and appropriate content security policy.
-   File upload size, type, content, and storage controls if uploads are
    introduced.
-   PII minimization and retention policy.
-   Audit logs protected against unauthorized modification.
-   Dependency and container vulnerability scanning.
-   No sensitive data, passwords, tokens, or payment details in logs.
-   Security review before public launch.

Create explicit automated tests for cross-tenant and cross-property
access.

------------------------------------------------------------------------

# 9. Reliability, data integrity, and performance

## 9.1 Data integrity

-   Use database foreign keys, unique constraints, check constraints,
    and indexes.
-   Use transactions for multi-step business operations.
-   Handle concurrent room assignment safely.
-   Use concurrency tokens / locking where needed.
-   Use idempotency for retried commands and background jobs.
-   Never rely on frontend validation for data integrity.
-   Use soft deletion only where business retention requires it; define
    how deleted records behave in reports and references.

## 9.2 Performance

Start with measured, indexed PostgreSQL queries rather than premature
caching.

-   Index common tenant/property filters.
-   Index room codes and room-type relationships.
-   Index reservation date ranges and statuses.
-   Use projection queries for list screens.
-   Avoid N+1 queries.
-   Paginate large result sets.
-   Set request timeouts and database command timeouts.
-   Cache only safe, scoped, invalidatable data.
-   Load-test availability and booking paths before launch.

Define measurable service targets after collecting baseline usage. Do
not invent performance guarantees before testing.

## 9.3 Background jobs

Use a maintained job scheduler only when recurring or asynchronous jobs
are introduced.

Every job must have:

-   Stable job identity
-   Retry policy with backoff
-   Idempotent behavior
-   Failure visibility and alerting
-   Explicit tenant / property scope
-   Cancellation and timeout behavior
-   Audit trail where it changes business data

------------------------------------------------------------------------

# 10. Testing strategy

## Unit tests

Cover:

-   Domain rules
-   Reservation state transitions
-   Date-range overlap
-   Room availability
-   Rental agreement overlap
-   Billing calculations
-   Permission policies
-   Idempotency behavior

## Integration tests

Two tiers, both required:

1. **In-memory tier** (`CustomWebApplicationFactory`) — fast, covers routing,
   authorization, tenant scope, and application-level business rules. It cannot
   verify transactions, exclusion constraints, or check constraints.
2. **PostgreSQL tier** (`PostgresTestFixture`, Testcontainers) — required for any
   assertion about a *database-level* guarantee. Runs the real migrations so the
   schema under test is the schema that ships. Skips (never silently passes) when
   Docker is unavailable.

Any claim of the form "the database prevents X" must be tested in tier 2.

Cover:

-   Migrations from an empty database
-   Tenant-scoped queries
-   Authorization
-   Transaction boundaries
-   Unique constraints
-   Concurrent room assignment
-   Booking cancellation
-   Check-in / check-out
-   Payment recording and reversal
-   Recurring billing idempotency

## Frontend tests

Cover:

-   Form validation
-   Permission-aware navigation
-   Loading / error / empty states
-   Organization switching and cache reset
-   Critical reservation workflows

## End-to-end tests

At minimum:

1.  Create organization and property.
2.  Create room types and rooms.
3.  Create a guest.
4.  Create and confirm a reservation.
5.  Assign a room.
6.  Check in.
7.  Record a charge and payment.
8.  Check out.
9.  Verify room operational status.
10. Attempt an overlapping booking and verify rejection.
11. Attempt cross-tenant access and verify denial.

## Quality gates

A change is not complete merely because it compiles.

Before marking a task complete:

-   Build succeeds.
-   Relevant unit tests pass.
-   Relevant integration tests pass.
-   Frontend lint / type-check / tests pass.
-   Migration is reviewed.
-   Authorization is reviewed.
-   No secrets are introduced.
-   API and docs are updated.
-   Acceptance criteria are demonstrated.

------------------------------------------------------------------------

# 11. DevOps and production operations

## Environments

Maintain separate:

-   Local development
-   Automated test / CI
-   Staging / UAT
-   Production

Never share production secrets or customer data with local development.

## Deployment

-   Build immutable application artifacts.
-   Pin and review dependency versions.
-   Run automated tests and security checks in CI.
-   Apply database migrations through a controlled release step.
-   Use health checks and readiness checks.
-   Support graceful shutdown.
-   Use rolling or low-downtime deployment where infrastructure supports
    it.
-   Have a rollback plan for application and database changes.
-   Do not assume database rollback is always safe; prefer forward-fix
    migrations for destructive schema changes.

## Observability

Include:

-   Structured JSON logs
-   Correlation IDs
-   Request latency and error metrics
-   Database health and connection metrics
-   Background job metrics
-   Distributed traces where useful
-   Alerts for elevated errors, failed jobs, database exhaustion, and
    backup failures

## Backup and recovery

Before launch, define:

-   Automated backup frequency
-   Retention period
-   Encryption
-   Off-site / separate-account storage
-   Point-in-time recovery if supported
-   Restore drill schedule
-   Recovery Point Objective (RPO)
-   Recovery Time Objective (RTO)

A backup is not verified until a restore has been tested.

## Operational documentation

Write runbooks for:

-   Deployment
-   Rollback
-   Database migration failure
-   Database restore
-   Suspected tenant data exposure
-   Authentication outage
-   Payment reconciliation issue
-   Background job failure
-   High database load

------------------------------------------------------------------------

# 12. Delivery roadmap

The following is a planning sequence, not a guaranteed calendar
estimate. Actual duration depends on team size, integrations, design
decisions, and acceptance criteria.

  Milestone   Status           Main outcome
  ----------- ---------------- ----------------------------------------------------------------
  M0          ✅ Complete      Requirements, domain glossary, ADRs (8 docs), CI
  M1          ✅ Complete      Auth, tenancy, organizations, properties, roles, audit
  M2          ✅ Complete      Buildings, floors, room types, rooms, operational status
  M3          ✅ Complete      Availability engine, guests, hotel reservations
  M4          ✅ Complete      Check-in/out, stay operations, housekeeping, night audit
  M5          ✅ Complete      Folios, charges, payments, reporting dashboard
  M6          ✅ Complete      Apartment tenants, rental agreements, meter readings, invoices
  M7          ✅ Complete      Branding resolver, subscriptions, entitlements, maintenance
  M7.5        ✅ Complete      EF Core migrations, PostgreSQL production schema (30 tables)
  M1.5        ✅ Complete      i18n architecture — react-i18next + 3 locale files (758 parity-checked keys) + user language preference + switcher + all 20 feature components migrated off hardcoded Thai (EPIC-51 ✅)
  M8          ✅ Complete      Concurrency hardening (EPIC-25/52 ✅); per-IP rate limiter (EPIC-53 ✅); on-premise installer guide ✅, TLS configuration guide ✅, backup runbook ✅ (docs/operations/)
  M9          ✅ Complete      Channel manager integration: ChannelConnection CRUD, ChannelRateMapping, StaahChannelManagerProvider (OTA_ResNotifRQ parser + HMAC-SHA256 webhook), IChannelAvailabilitySync, inbound webhook endpoint, manual sync trigger, 23 integration tests (Phase9ChannelManagerTests)
  M10         ✅ Complete      Rate plan CRUD (weekday/weekend/seasonal/monthly/min-stay/blackout), coupon system (percentage/fixed/free-night, server-side validation, usage tracking), group bookings (create/link reservations/cancel propagation), reservation rate locking (quoted rate never recalculated), staff-only rate plan access control, 20 integration tests (Phase10RatePlanTests)
  M10.5       ✅ Complete      Ancillary services (ServiceItem CRUD), meal packages (Package + PackageItem with MealPlanType), quotations (create/convert-to-reservation/cancel, duplicate-convert guard), ADR + RevPAR KPI endpoint, occupancy and revenue XLSX export (ClosedXML), 15 integration tests (Phase10_5Tests)
  M11         ✅ Complete      Thai compliance: Guest extended (DateOfBirth/VisaType/CountryOfOrigin), Property.EnableThaiCompliance flag, ThaiComplianceRecord (immutable per-stay snapshot) + ThaiComplianceAmendment (before/after audit), R.S.3/R.S.4/TM.30 Excel exports (ClosedXML, Thai-language headers), one record per reservation (409 guard), foreignersOnly filter for TM.30, EF migration (AddThaiCompliance), 13 integration tests (Phase11ThaiComplianceTests) — 140/140 total tests pass
  M12         ✅ Complete      Email automation: IEmailService + EmailOptions (SmtpEmailService/NullEmailService), EmailTemplates (booking confirmation + daily digest HTML+text), EmailNotificationSettings per property (enable/disable confirmation, daily report recipients + UTC send hour), booking confirmation sent on ReservationConfirm (synchronous, exception-swallowed), DailyReportBackgroundService (hourly tick + manual trigger endpoint), EF migration (AddEmailNotificationSettings), NullEmailService injected in test factory for capture assertions, 9 integration tests (Phase12EmailNotificationTests) — 149/149 total tests pass
  M13         ✅ Complete      Direct booking engine: BookingEngineSettings (IsEnabled/HoldDurationMinutes/advance-window), BookingHold (server-side timer lock with SessionToken/ExpiresAt/HoldStatus), public endpoints (GET availability with public-rate filtering, POST validate-coupon, POST hold, DELETE hold, POST reservations), BookingHoldCleanupService (5-min background expiry), "booking" rate limiter (60/min per IP), EF migration (AddBookingEngine), InvariantCulture date parsing (th-TH locale fix), 12 integration tests (Phase13BookingEngineTests) — 161/161 total tests pass
  M14         ✅ Complete      Security hardening: password policy (8+ chars, letter+digit required), HSTS header (production), Cache-Control: no-store on auth endpoints, HTTPS redirect (non-dev), 10 MB global request body limit, 12 security integration tests (Phase14SecurityTests: weak-password policy, unauthenticated access, tampered JWT, cross-tenant IDOR on property/reservation/guest, security headers, auth cache headers, logo upload magic-bytes enforcement) — 173/173 total tests pass

**Deployment strategy:** On-premise is the primary commercial model.
Install on the hotel's own server. SaaS capability is preserved in the
codebase via DeploymentMode configuration but is not the target for
the first commercial release.

**Recommended next steps in order:**

1. M8 — On-premise: concurrency hardening complete; finish installer
   documentation, TLS configuration guide, backup runbook.
2. M9 — Channel manager: STAAH outbound sync implementation, inbound
   webhook receiver, rate mapping UI.
3. M10 — Rate management: rate plans, pricing rules, promotions, coupons,
   group bookings, drag-and-drop calendar enhancements.
4. M10.5 — Ancillary services, meal packages, quotations, ADR/RevPAR
   reporting metrics, Excel export.
5. M11 — Thai compliance: R.S.3, R.S.4, TM.30 generation and export.
6. M12 — Email automation: booking confirmation, payment notice, daily
   report digest.
7. M13 — Direct booking engine.
8. M14 — Pilot hotel UAT, security review, production go-live.

------------------------------------------------------------------------

# 13. Initial backlog

Use these epics and break each into small issues with acceptance
criteria.

-   EPIC-01: Repository and developer experience
-   EPIC-02: Identity and authentication
-   EPIC-03: Organizations and tenant isolation
-   EPIC-04: Property configuration
-   EPIC-05: Buildings and floors
-   EPIC-06: Room types and room inventory
-   EPIC-07: Room operational status
-   EPIC-08: Guest / tenant profiles
-   EPIC-09: Availability engine
-   EPIC-10: Hotel reservations
-   EPIC-11: Room allocation and stay operations
-   EPIC-12: Check-in and check-out
-   EPIC-13: Basic folios and payments
-   EPIC-14: Housekeeping
-   EPIC-15: Apartment rental agreements
-   EPIC-16: Recurring billing and utilities
-   EPIC-17: Reporting
-   EPIC-18: Audit and compliance
-   EPIC-19: Subscriptions and entitlements
-   EPIC-20: On-premise packaging and release readiness
-   EPIC-21: Channel manager provider abstraction
-   EPIC-22: STAAH channel manager integration
-   EPIC-23: SiteMinder channel manager integration
-   EPIC-24: OTA booking source tracking and reporting
-   EPIC-25: Concurrent booking conflict prevention (database-level)
-   EPIC-26: Email notifications (booking confirmation, check-in reminder, daily report)
-   EPIC-27: On-premise installer and upgrade guide
-   EPIC-28: Rate plan management (weekday/weekend/seasonal/monthly, min stay, blackout)
-   EPIC-29: Staff-only and agent rate plans
-   EPIC-30: Promotions engine (early bird, last minute, long stay, free night)
-   EPIC-31: Coupon / discount code system
-   EPIC-32: Group booking management
-   EPIC-33: Booking holds and temporary room locks
-   EPIC-34: Drag-and-drop availability calendar with change summary
-   EPIC-35: Ancillary services and meal plan packages
-   EPIC-36: Quotations (pre-booking estimates, convertible to reservations)
-   EPIC-37: Tax invoice generation with running receipt numbers
-   EPIC-38: ADR, RevPAR, and occupancy KPI dashboard
-   EPIC-39: Excel / CSV report export
-   EPIC-40: Automated daily email report
-   EPIC-41: Thai government compliance — R.S.3 registration card
-   EPIC-42: Thai government compliance — R.S.4 registration summary
-   EPIC-43: Thai government compliance — TM.30 foreign guest report
-   EPIC-44: Government Excel export (DOPA and Immigration Bureau formats)
-   EPIC-45: Direct booking engine — availability and rate display
-   EPIC-46: Direct booking engine — payment hold timer and auto-confirmation
-   EPIC-47: Direct booking engine — coupon redemption and booking policies
-   EPIC-48: Dorm / shared room type support (hostel use case)
-   EPIC-49: Internationalisation foundation — react-i18next setup, Thai and English locale files, user language preference stored on User entity, language switcher UI ✅ done
-   EPIC-50: Chinese Simplified (zh-CN) locale — draft translation in place, needs native-speaker review before a Chinese customer goes live
-   EPIC-51: Migrate remaining 20 feature components from hardcoded Thai to i18n keys ✅ done
    — all 20 components (ApartmentManager, AuditLogView, AvailabilityCalendar,
    BillingManager, BuildingFloorManager, ChannelManagerView, CreateRoomModal,
    DashboardView, GuestManager, HousekeepingView, MaintenanceView,
    PropertySettingsForm, ReservationManager, RoomDetailModal, RoomRack,
    RoomTypeManagerModal, SettingsPage, StayOperationsView, TeamManagement,
    WalkInWizard) now call `t()` via `useTranslation()`; en/th/zh-CN locale
    files hold 813 keys each with verified 1:1 parity; `tsc --noEmit` and
    `vite build` pass clean. Post-review caught 58 keys that one migration
    pass had referenced via `t()` without ever adding to the locale files
    (mostly in ReservationManager.tsx) — i18next silently renders the raw
    key string when a lookup misses, so these showed up as literal text
    like `reservations.todayArrivals` in the UI instead of a visible
    error. Fixed by auditing every `t('...')` call against the actual
    locale JSON and adding the missing keys in all three languages; a
    repo-wide grep for Thai Unicode ranges plus this key-usage audit is
    the way to re-verify this class of gap after any future i18n change.
    A few admin-only labels with no obvious
    matching key (e.g. some BuildingFloorManager/PropertySettingsForm field
    chrome, SettingsPage capability labels) were left in English rather than
    inventing new keys — revisit if a customer needs those translated.
-   EPIC-52: PostgreSQL-backed concurrency tests (Testcontainers) proving the
    `no_room_double_booking` exclusion constraint and Serializable isolation ✅ done
    — the fast in-memory suite remains for everything else
-   EPIC-53: Partition the auth and api rate limiters per client IP / per user ✅ done
    — `RateLimitPartitionKey` helper (IPv4, IPv6/64, IPv4-mapped-IPv6 unwrapping);
    `ForwardedHeaders` middleware opt-in via `ReverseProxy:KnownProxies` /
    `KnownNetworks` (default off — X-Forwarded-For is not blindly trusted);
    13 unit tests + 2 integration tests; `appsettings.json` and `.env.example`
    updated with operator guidance

For every issue define: problem, actor, preconditions, acceptance
criteria, authorization, data changes, failure cases, tests, and
documentation updates.

------------------------------------------------------------------------

# 14. Production release checklist

Do not release publicly until the following are true:

-   [ ] Product workflows validated with real hotel / apartment
    operators. *(requires pilot hotel UAT)*
-   [x] Tenant isolation tests pass. *(Phase1IntegrationTests + Phase14SecurityTests — IDOR on property/reservation/guest verified)*
-   [x] Role and property permission tests pass. *(Phase1IntegrationTests — cross-org access denied; Phase14SecurityTests — 401/403/404 verified)*
-   [x] Booking concurrency tests pass. *(PostgresConcurrencyTests — exclusion constraint + serializable isolation; Phase8IntegrationTests)*
-   [x] Financial calculations and reconciliation are verified. *(Phase5IntegrationTests — folio charges, payments, reversal; Phase10_5Tests — ADR/RevPAR)*
-   [x] Database constraints and indexes reviewed. *(EF migrations reviewed; unique indexes on room codes, booking holds, rate plans, coupons; exclusion constraint on reservations)*
-   [ ] Migrations tested against staging-like data volume. *(requires real PostgreSQL with realistic row counts — run before go-live)*
-   [x] No critical or high unresolved security findings. *(M14 security hardening complete: password policy, HSTS, Cache-Control, HTTPS redirect, body limit, IDOR tests)*
-   [ ] Secrets and TLS configured. *(requires production server — see docs/operations/tls-configuration.md)*
-   [ ] Backups configured and restore drill completed. *(requires production server — see docs/operations/backup-runbook.md)*
-   [ ] Monitoring and alerts configured. *(requires production server — /healthz/live and /healthz/ready endpoints are ready)*
-   [x] Error and incident runbooks written. *(docs/operations/: deployment, rollback, database-migration-failure, tenant-data-exposure, authentication-outage, payment-reconciliation, background-job-failure, high-database-load)*
-   [ ] Privacy policy, terms, data retention, and customer data
    export/deletion procedures reviewed. *(requires legal review)*
-   [ ] Thai compliance documents (R.S.3, R.S.4, TM.30) validated by
    a qualified person before any Thai hotel goes live. *(requires qualified accountant/legal review)*
-   [x] Rate plan and promotion calculations verified against test
    scenarios. *(Phase10RatePlanTests — weekday/weekend/seasonal rates, min-stay, blackout, coupons, group pricing)*
-   [x] ADR and RevPAR metrics reconcile with underlying reservation
    and revenue data. *(Phase10_5Tests — ADR/RevPAR KPI endpoint tested against seeded reservation data)*
-   [x] Excel exports validated in Thai locale. *(ClosedXML exports use explicit CultureInfo.InvariantCulture; date parsing in public endpoints uses InvariantCulture — th-TH Buddhist Era trap documented in memory)*
-   [ ] Email delivery (confirmation, daily report) verified end-to-end. *(requires real SMTP — NullEmailService in tests; SmtpEmailService in production; configure EmailOptions in .env)*
-   [ ] UAT sign-off obtained. *(requires pilot hotel stakeholders)*
-   [ ] Support and incident ownership defined. *(business decision — assign a named person for each incident type)*
-   [x] Rollback / forward-fix plan documented. *(docs/operations/rollback-runbook.md)*

------------------------------------------------------------------------

# 15. Decisions that must be confirmed before implementation

Do not silently invent business policy. Record decisions in `docs/adr/`
or product requirements.

1.  First pilot customer: hotel, apartment, or both?
2.  Is each customer organization allowed multiple properties?
3.  Does a hotel reservation require room assignment at booking time or
    may it remain room-type-only?
4.  Hotel date policy: property check-in / check-out time, early
    arrival, late departure, and night audit.
5.  Apartment agreement date policy and billing day.
6.  Currency and tax requirements for the first market.
7.  Payment recording only, or online payment gateway?
8.  Authentication method: managed OIDC provider or application-managed
    Identity?
9.  Hosting provider, region, budget, and data residency needs.
10. Email / LINE notification provider and consent policy.
11. Import requirements for existing room and tenant data.
12. User roles and property-level permission matrix.
13. Will the product support both SaaS and On-premise from the first
    commercial release?
14. What branding customization is included in each subscription plan?
15. Can customers hide the "Powered by StayFlow" attribution?
16. Can one organization configure different branding for each property?
17. Will custom domains be supported in the future?
18. What is the on-premise license activation and renewal policy?
19. Which payment providers will be integrated in the booking engine
    (PromptPay QR, credit card gateway, e-wallet)?
20. Will rate plans support multi-currency display, or only property
    default currency?
21. Is the direct booking engine a separate subdomain / subdirectory,
    or embedded via iframe/widget?
22. Must the booking engine support dorm / shared room types
    (hostel use case) in the first release?
23. Which government system format is currently accepted for TM.30
    (online submission format or Excel upload)?
24. Should group booking support a shared invoice, split invoices, or
    both?
25. What is the email provider (SMTP relay, SendGrid, AWS SES, LINE
    Notify) for transactional emails and daily reports?
26. Should the direct booking engine (M13) support multilingual display
    for guests (Thai, English, Chinese) independently of the
    back-office language setting?
27. Is Chinese Traditional (zh-TW, Taiwan / Hong Kong market) required
    in addition to Simplified (zh-CN)?

Where a decision is not yet known, choose the simplest safe default,
document it as provisional, and isolate it so it can be changed without
invasive rewrites.

------------------------------------------------------------------------

# 16. Working agreement for Claude CLI

Claude must follow the instructions below for every task in this
repository.

## 16.1 Role

Act as a senior software architect and pragmatic full-stack engineer.
Optimize for correctness, maintainability, security, and incremental
delivery---not maximum code volume.

## 16.2 Before changing code

1.  Read this file completely.
2.  Inspect the repository structure, existing code, package versions,
    and project conventions.
3.  Read relevant product requirements, ADRs, and module documentation.
4.  Identify the exact user story and acceptance criteria.
5.  Trace affected frontend, API, domain, persistence, and test paths.
6.  Present a concise implementation plan for changes spanning multiple
    modules or involving security / data migrations.
7.  Ask for clarification only when a missing decision materially
    changes correctness or creates a risky assumption. Otherwise
    document a conservative assumption and proceed.

## 16.3 Implementation rules

-   Implement in small, reviewable increments.
-   Follow existing conventions unless they are demonstrably unsafe or
    inconsistent.
-   Do not rewrite unrelated files.
-   Do not perform broad refactors without explicit need.
-   Do not add abstractions, packages, services, or infrastructure
    without a concrete use case.
-   Keep business rules out of controllers and React components.
-   Keep API contracts explicit and use DTOs.
-   Validate input at the API boundary and enforce domain invariants in
    the application/domain layer.
-   Enforce tenant and property authorization server-side.
-   Use transactions for multi-step business operations.
-   Protect booking and room allocation from race conditions.
-   Use decimal for money and explicit currency.
-   Use UTC for timestamps and property time zones for local business
    rules.
-   Use migrations for schema changes; never modify production data
    manually as an application shortcut.
-   Add tests for every business rule and bug fix.
-   Update OpenAPI, README, ADRs, and module docs when behavior or
    architecture changes.
-   Make accessibility, responsive design, loading states, and error
    states part of frontend completion.
-   Do not claim a feature is complete if it is only a mock,
    placeholder, or happy-path implementation.

## 16.4 Prohibited actions

-   Never hardcode secrets, credentials, tokens, connection strings, or
    private keys.
-   Never commit `.env` files containing secrets.
-   Never auto-push, force-push, create a release, or deploy to
    production unless explicitly instructed.
-   Never delete user data or run destructive database commands without
    explicit approval.
-   Never bypass authentication, authorization, validation, or tests to
    make a demo work.
-   Never disable failing tests without explaining the reason and
    obtaining approval.
-   Never fabricate test results, API responses, integrations, or
    deployment success.
-   Never introduce fake payment processing or claim an offline-recorded
    payment was processed online.
-   Never expose stack traces, secrets, or sensitive personal data in
    user-facing errors or logs.
-   Never use tenant IDs from client input as proof of tenant access.
-   Never silently change business rules or financial calculations.

## 16.5 Dependency and tooling policy

-   Prefer maintained, compatible, well-supported packages.
-   Check whether an existing dependency already solves the problem.
-   Explain why a new dependency is needed.
-   Pin versions according to repository policy and commit lockfiles.
-   Do not upgrade unrelated packages during feature work.
-   Do not assume a cloud service is free or available; document cost
    and operational implications.
-   Use local Docker services for development when practical, but keep
    production configuration separate.

## 16.6 Required workflow for every task

1.  **Understand:** Read relevant instructions and inspect existing
    implementation.
2.  **Plan:** State scope, files/modules likely affected, risks, and
    acceptance criteria.
3.  **Implement:** Make the smallest complete change.
4.  **Test:** Run relevant tests, lint, type checks, and builds.
5.  **Review:** Inspect diff for unrelated changes, security gaps, and
    missing error paths.
6.  **Document:** Update relevant documentation and migration notes.
7.  **Report:** Summarize files changed, behavior delivered, commands
    run, actual test outcomes, known limitations, and next step.

If a command cannot run because a tool or service is unavailable, report
it explicitly. Do not imply it passed.

## 16.7 Definition of done

A task is done only when:

-   Acceptance criteria are met.
-   Backend and frontend behavior are integrated where applicable.
-   Authorization and tenant boundaries are enforced.
-   Relevant automated tests are added and pass.
-   Build, lint, and type checks pass where applicable.
-   Database migration is included and reviewed where needed.
-   Error, loading, empty, and edge cases are handled.
-   Documentation is updated.
-   No unrelated changes or secrets are present.
-   The final report accurately describes verification and remaining
    risks.

## 16.8 Claude response format

For implementation tasks, respond with:

1.  **Understanding** --- what is being implemented.
2.  **Plan** --- concise ordered steps.
3.  **Changes** --- files and modules changed.
4.  **Verification** --- exact commands and actual results.
5.  **Risks / limitations** --- unresolved decisions or failures.
6.  **Next task** --- the smallest logical next increment.

For a large feature, do not attempt the entire product in one response
or one unreviewed batch. Break it into milestones and stop at
meaningful, testable checkpoints.

------------------------------------------------------------------------

# 17. First task to give Claude

After placing this file at the repository root as `Claude.md`, use the
following initial prompt:

``` text
Read Claude.md completely and follow it as the project-level source of truth.

We are starting StayFlow PMS, a multi-tenant SaaS Property Management System for hotels and apartments.

Do not implement the whole product yet.

Task 1 — Repository and architecture baseline:
1. Inspect the current repository and available SDK/tool versions.
2. Identify existing code and conventions; do not overwrite or delete existing work.
3. Propose the initial solution structure for ASP.NET Core 10, React + TypeScript, and PostgreSQL.
4. Create the minimum runnable backend and frontend skeleton only if the repository does not already contain them.
5. Add local development configuration using environment variables and safe example files only.
6. Add Docker Compose for local PostgreSQL and any dependency that is genuinely required now.
7. Add health checks, basic structured logging, OpenAPI, and a minimal CI workflow.
8. Add a concise README with exact setup, migration, run, and test commands.
9. Add ADRs for modular monolith, shared-database multi-tenancy, authentication approach, and date/time handling. Mark undecided choices as provisional rather than pretending they are final.
10. Add tests for the initial health endpoint and any foundational behavior.

Constraints:
- Do not implement room, reservation, billing, or apartment business features in this task.
- Do not add Redis unless there is an immediate, justified use case.
- Do not add unnecessary packages or empty modules.
- Do not hardcode secrets.
- Do not auto-push or deploy.
- Do not claim production readiness yet.

Before coding, report your repository findings and implementation plan. Then implement the smallest complete baseline, run the available checks, inspect the diff, and report exact results and remaining gaps.
```

------------------------------------------------------------------------

# 18. Final implementation strategy

Build the product in this order:

**Platform foundation → Multi-tenancy → Property setup → Room management
→ Availability → Hotel reservations → Stay operations → Billing →
Apartment agreements → SaaS operations.**

The most important engineering risks are tenant data leakage, double
booking, incorrect date boundaries, and inaccurate financial records.
Prioritize these over visual polish or advanced integrations.

A working room list is a demo. A system that reliably prevents
conflicting room allocations, enforces customer data isolation, records
changes, and can be backed up and restored is the beginning of a
commercial PMS.

# 19. Unified Deployment Architecture — SaaS and On-premise

## 19.1 Core principle

StayFlow PMS must support SaaS and On-premise deployment using one codebase.

Do not create separate codebases, branches, or customer-specific forks for different deployment models.

The application must separate business logic from deployment-specific infrastructure.

## 19.2 SaaS deployment

SaaS is the vendor-hosted deployment model.

Requirements:

- Support multiple customer organizations.
- Enforce strict tenant isolation.
- Support subscription plans and feature entitlements.
- Support organization and property management.
- Provide centralized application monitoring.
- Support controlled application upgrades.
- Support tenant provisioning and deprovisioning.
- Support customer data export and deletion workflows.
- Support backup and recovery procedures.
- Allow customers to configure their own branding according to entitlements.

## 19.3 On-premise deployment

On-premise is the customer-managed deployment model.

Requirements:

- Run the same application modules as SaaS.
- Support a dedicated customer database.
- Support customer-controlled infrastructure.
- Provide installation and upgrade packages.
- Support license activation and validation.
- Support configurable storage locations.
- Support customer-managed backups.
- Support offline operation where technically and commercially required.
- Provide installation, troubleshooting, and recovery documentation.

The application must not require continuous internet connectivity unless a specific external integration requires it.

## 19.4 Deployment configuration

Use strongly typed configuration.

Example conceptual configuration:

- DeploymentMode: SaaS or OnPremise
- LicenseValidationEnabled
- ExternalStorageProvider
- EmailProvider
- PaymentProvider
- FeatureEntitlementProvider

Never use deployment-mode checks throughout domain entities or business rules.

Keep deployment-specific infrastructure behind interfaces.

## 19.5 Deployment acceptance criteria

- Both deployment modes use the same business domain model.
- SaaS tenant isolation is verified.
- On-premise organization isolation is verified.
- Deployment configuration is validated at startup.
- Missing required configuration produces clear errors.
- On-premise installation and upgrade procedures are documented.
- No customer-specific source-code forks are required.

---

# 20. White-label Branding

White-label branding is a core product capability.

Customers must be able to present StayFlow PMS as their own branded property-management system without requiring a separate application or source-code modification.

## 20.1 Branding levels

Support three branding levels:

1. Platform Branding — StayFlow PMS, the default vendor identity.
2. Organization Branding — The customer company or property operator.
3. Property Branding — The individual hotel or apartment identity.

An organization may operate multiple properties, and each property may have a different brand.

Example:

Organization: ABC Hospitality Group

Properties:
- ABC Grand Hotel
- ABC Residence
- ABC Serviced Apartment

Each property can have its own display name, logo, and theme.

## 20.2 Branding inheritance

Use this precedence:

Property Override → Organization Branding → Platform Default

Rules:

- Property-level settings override organization-level settings.
- Unset property values inherit organization values.
- Unset organization values inherit platform defaults.
- Do not duplicate inherited values into property records.
- Branding resolution must use the authenticated user's authorized organization and active property.

## 20.3 Configurable branding fields

Support:

- DisplayName
- LogoUrl
- FaviconUrl
- PrimaryColor
- SecondaryColor
- LoginTitle
- LoginWelcomeText
- FooterText
- SupportEmail
- SupportPhone
- CustomDomain (future capability)

All fields must have validation and safe defaults.

Do not allow customer-provided HTML or JavaScript.

## 20.4 Branding database model

Create a BrandingSettings entity/table.

Suggested fields:

- Id
- OrganizationId (required)
- PropertyId (nullable)
- DisplayName
- LogoUrl
- FaviconUrl
- PrimaryColor
- SecondaryColor
- LoginTitle
- LoginWelcomeText
- FooterText
- SupportEmail
- SupportPhone
- CreatedAt
- CreatedBy
- UpdatedAt
- UpdatedBy
- RowVersion / ConcurrencyToken

Database rules:

- Every branding record belongs to exactly one organization.
- PropertyId must belong to the same OrganizationId.
- Only one organization-level branding record per organization.
- Only one property-level branding record per property.
- Use PostgreSQL partial unique indexes where appropriate.
- Add foreign keys and indexes.
- Enforce tenant scope on every query and command.
- Record branding changes in AuditEvents.

## 20.5 Branding resolver

Create a centralized backend service:

IBrandingResolver

Responsibilities:

1. Resolve the authenticated organization.
2. Resolve the authorized active property.
3. Load property-specific branding.
4. Fall back to organization branding.
5. Fall back to platform defaults.
6. Return an effective branding DTO to the frontend.

Do not implement separate branding inheritance logic in multiple controllers or frontend components.

If caching is used:

- Include OrganizationId and PropertyId in cache keys.
- Invalidate cache after branding updates.
- Prevent branding from one tenant appearing in another tenant's session.

## 20.6 Branding management UI

Provide:

Settings → Branding & Appearance

Authorized administrators can:

- Change system display name.
- Upload or replace logo.
- Upload or replace favicon.
- Configure primary and secondary colors.
- Configure login title and welcome text.
- Configure footer information.
- Configure support email and phone.
- Preview branding before saving.
- Reset individual fields to inherited values.
- Reset all branding to defaults.

The interface must clearly show whether a value is inherited or overridden.

Provide Thai and English translations.

## 20.7 Branding security

- Validate actual file content, not only file extensions.
- Allow explicitly supported image formats such as PNG, JPEG, and WebP.
- Reject SVG by default unless safe sanitization is implemented.
- Enforce image size and dimension limits.
- Generate server-side storage filenames.
- Prevent path traversal.
- Prevent cross-tenant asset access.
- Sanitize all text inputs.
- Validate colors using a strict format such as #RRGGBB.
- Restrict branding changes to users with Branding.Manage permission.
- Audit every branding change.

For unauthenticated login pages, use platform branding by default.

Do not trust arbitrary organization IDs or property IDs from query strings to select a tenant's branding.

Tenant-specific login branding may be introduced later through verified organization slugs or validated custom-domain mappings.

## 20.8 Custom domains

Custom domains are future scope.

If implemented:

- Verify domain ownership.
- Map verified domains to organizations server-side.
- Require HTTPS/TLS.
- Validate host headers.
- Prevent domain takeover.
- Define domain removal and reassignment rules.

Do not implement custom domains in the initial MVP unless explicitly approved.

## 20.9 Commercial branding entitlements

Keep branding features separate from hardcoded subscription plan names.

Example capability keys:

- branding.organization
- branding.property
- branding.customDomain
- branding.hideVendorAttribution

Possible commercial policy:

- Basic: Organization display name and logo.
- Professional: Property-specific branding and theme colors.
- Enterprise: Advanced branding and optional custom domains.
- On-premise: Branding configuration according to purchased license.

The actual plan mapping and pricing must be decided by the product owner.

The "Powered by StayFlow" attribution policy must be explicit.

## 20.10 Branding acceptance criteria

- New organizations receive platform-default branding.
- Organization administrators can configure branding.
- Authorized property administrators can override branding.
- Inheritance works correctly.
- Cross-tenant branding access is denied.
- Switching properties updates effective branding.
- Branding changes are audited.
- Invalid uploads and unsafe content are rejected.
- Branding works in Thai and English.
- SaaS and On-premise use the same branding domain model.

---

# 21. Subscription, Entitlement, and License Architecture

## 21.1 Subscription model

SaaS subscriptions must use capability-based entitlements.

Suggested entities:

- SubscriptionPlans
- Subscriptions
- SubscriptionItems
- Entitlements
- PlanEntitlements
- SubscriptionHistory
- UsageRecords

Do not hardcode plan names or pricing inside business logic.

## 21.2 Feature entitlement examples

Use stable capability keys:

- hotel.reservations
- hotel.housekeeping
- hotel.nightAudit
- apartment.rentals
- apartment.utilities
- finance.invoices
- reporting.advanced
- branding.organization
- branding.property
- branding.customDomain
- users.max
- properties.max

Feature access must be checked server-side.

Frontend visibility is not a security boundary.

## 21.3 On-premise licensing

On-premise deployments must support signed licenses.

A license should define:

- LicenseId
- Customer reference
- Licensed capabilities
- Maximum users
- Maximum properties
- Issue date
- Expiration date
- Product version constraints
- Signature

Requirements:

- Use asymmetric digital signatures.
- Keep private signing keys outside the application and repository.
- Verify licenses using a trusted public key.
- Define activation, renewal, expiration, and revocation behavior.
- Support offline validation if required.
- Log license validation events without exposing sensitive data.
- Provide a safe grace/read-only policy for expired licenses.

Never trust an unsigned editable JSON file as a secure license.

Never implement fake license validation.

License failure must not silently delete customer data.

---

# 22. Database Integrity and Multi-tenant Data Protection

## 22.1 Organization ownership

Every tenant-owned record must have a clear organization ownership path.

For security-sensitive or high-volume entities, prefer explicit OrganizationId.

Examples:

- Properties
- Buildings
- Floors
- Rooms
- Guests
- Reservations
- Stays
- Tenants
- RentalAgreements
- Folios
- Invoices
- Payments
- BrandingSettings
- Attachments

## 22.2 Tenant enforcement

Enforce tenant scope at:

- Authentication context
- Application use-case layer
- Data access layer
- Background jobs
- File storage
- Reports
- Exports
- Notifications

Never rely on frontend filtering.

Never trust OrganizationId from a request body as authorization evidence.

## 22.3 Financial integrity

- Use decimal/numeric types for monetary values.
- Store currency explicitly.
- Define rounding rules.
- Use immutable posted financial records.
- Use reversal and adjustment records for corrections.
- Use database transactions for financial posting.
- Use idempotency keys for retryable operations.
- Protect against duplicate invoice generation.
- Reconcile payments against invoices and folios.
- Audit all financial state changes.

## 22.4 Date and timezone integrity

- Store timestamps in UTC.
- Store each property's IANA timezone.
- Use explicit business dates for hotel operations.
- Define apartment billing periods explicitly.
- Test date boundaries and timezone transitions.
- Do not use the server timezone as the property's business timezone.

## 22.5 Database acceptance criteria

- Tenant-scoped queries are verified.
- Cross-tenant reads and writes are denied.
- Financial transactions are atomic.
- Duplicate recurring billing is prevented.
- Room allocation concurrency is protected.
- Database constraints enforce critical business invariants.
- Migrations work from a clean database.
- Upgrade migrations are tested.

---

# 23. Security and Privacy Hardening

Use OWASP ASVS and OWASP API Security Top 10 as security review references.

Requirements:

- Secure authentication and session management.
- Server-side authorization.
- Organization and property scope validation.
- Protection against IDOR and broken object-level authorization.
- Input validation and output encoding.
- Parameterized SQL.
- Secure file upload and download.
- Rate limiting.
- Secure CORS configuration.
- CSRF protection when cookie authentication is used.
- TLS for non-local environments.
- Secret management.
- Dependency vulnerability scanning.
- Audit log protection.
- Personal data minimization.
- Data retention and deletion policies.
- Backup encryption.
- Restore verification.
- No secrets or sensitive guest/tenant data in logs.

Sensitive operations must have explicit permissions.

Do not claim legal or regulatory compliance without a documented assessment.

---

# 24. Production Readiness and Operational Requirements

Production readiness includes application code, infrastructure, data integrity, security, support, and recovery.

## 24.1 Monitoring

Provide:

- Application health checks.
- Database health checks.
- Structured logs.
- Correlation IDs.
- Request latency metrics.
- Error-rate metrics.
- Background job monitoring.
- Backup failure alerts.
- License validation monitoring for on-premise.

## 24.2 Backup and restore

Backup must include:

- Database
- Uploaded logos and branding assets
- Required customer configuration
- Relevant application-managed files

Requirements:

- Define backup frequency and retention.
- Encrypt backups.
- Restrict backup access.
- Test restoration periodically.
- Document recovery procedures.
- Define RPO and RTO with the product/operations owner.

A backup is not considered verified until a restore has been tested.

## 24.3 SaaS operational requirements

- Tenant provisioning.
- Tenant deprovisioning.
- Subscription lifecycle.
- Tenant data export.
- Tenant data deletion.
- Application upgrades.
- Monitoring and incident response.
- Backup and disaster recovery.
- Support access with strict authorization and audit.

## 24.4 On-premise operational requirements

Provide:

- Installation guide.
- Supported environment matrix.
- Configuration guide.
- License activation guide.
- Upgrade guide.
- Backup and restore guide.
- Log collection guide.
- Troubleshooting guide.
- Offline update process if required.

## 24.5 Release acceptance criteria

Do not release publicly until:

- Critical hotel workflows pass.
- Critical apartment workflows pass.
- Tenant isolation tests pass.
- Reservation concurrency tests pass.
- Financial integrity tests pass.
- Branding inheritance tests pass.
- Authorization tests pass.
- Database migrations are verified.
- Backup and restore are verified.
- Security review is completed.
- UAT sign-off is obtained.
- Operational documentation is complete.

---

# 25. Updated Implementation Roadmap

The original roadmap remains valid. The following items extend it.

## M1 — Platform Foundation

Add:

- Deployment mode configuration.
- Organization and property ownership rules.
- Tenant authorization foundation.
- Platform default branding.
- **Internationalisation (i18n) architecture:** react-i18next, Thai and
  English locale JSON files, `PreferredLanguage` field on User entity
  (`th` | `en` | `zh-CN`), language switcher in the top bar, backend
  error/validation messages localised via `Accept-Language` header.
  All UI strings must be externalised into locale files from day one.
  Chinese Simplified locale file is scaffolded (empty keys) but
  translations are deferred to EPIC-50.
- Audit foundation.

## M2 — Property Management

Add:

- Organization branding.
- Property branding overrides.
- Branding resolver.
- Branding management UI.
- Logo and favicon upload.
- Branding audit events.

## M3–M6 — Core Business Modules

Ensure all hotel and apartment operations:

- Respect organization and property boundaries.
- Use correct business-date rules.
- Support localized UI.
- Generate auditable financial records.
- Apply server-side entitlements where appropriate.

## M7 — Commercial and Deployment

Add:

- SaaS subscriptions.
- Capability entitlements.
- On-premise signed licensing.
- Customer installation package.
- Upgrade procedures.
- Backup and restore documentation.
- Branding entitlement enforcement.

## M8 — Production Hardening

Add:

- Security assessment.
- Performance and concurrency tests.
- Tenant isolation verification.
- Branding isolation verification.
- Backup and restore drill.
- UAT.
- Production release review.

Do not mark any milestone complete because the UI exists alone.

Require implementation, automated tests, code review, documentation, and operational verification.

---

# 26. Additional Claude CLI Engineering Rules

These rules apply to every implementation task.

## 26.1 Repository inspection

Before changing code:

1. Inspect repository structure.
2. Check current Git branch and working tree.
3. Read Claude.md completely.
4. Read README and relevant architecture documents.
5. Inspect existing code and dependencies.
6. Identify implemented, partially implemented, planned, and unknown features.
7. Report conflicts between the repository and this specification.

Do not assume that the repository is empty or that the planned architecture has already been implemented.

## 26.2 Implementation discipline

- Work in small, reviewable increments.
- Do not implement all milestones in one task.
- Do not rewrite unrelated modules.
- Do not add unnecessary packages.
- Do not introduce microservices without an ADR.
- Do not add Redis without a justified use case.
- Do not hardcode secrets, tenant IDs, or customer-specific branding.
- Do not push or deploy without explicit approval.
- Do not run destructive database operations without explicit approval.
- Do not claim tests passed unless they were actually executed.

## 26.3 White-label implementation rules

- Branding is presentation configuration only.
- Do not rename internal product identifiers, namespaces, database objects, or API routes based on customer branding.
- Implement one backend branding resolver.
- Follow Property → Organization → Platform inheritance.
- Validate ownership before resolving or updating branding.
- Use safe platform defaults.
- Never trust arbitrary query parameters or Host headers for tenant discovery.
- Test inheritance, permissions, tenant isolation, upload security, and cache invalidation.
- Do not implement custom domains unless explicitly approved.

## 26.4 Documentation

When architecture, database, security, or deployment behavior changes:

- Update relevant documentation.
- Update ADRs.
- Update database schema documentation.
- Update API contracts.
- Update migration notes.
- Update backlog status.
- Record unresolved decisions as pending.

## 26.5 Definition of Done

A task is complete only when:

- Acceptance criteria are satisfied.
- Server-side authorization is implemented.
- Tenant isolation is enforced.
- Database constraints and transactions are correct.
- Relevant automated tests are added and pass.
- Build and lint checks pass where applicable.
- Error and edge cases are handled.
- Documentation is updated.
- No unrelated changes are included.
- Verification results are accurately reported.

---

# 27. First Task After Updating This Specification

Do not immediately implement the entire PMS.

First perform a repository and architecture assessment.

The assessment must include:

1. Current repository structure.
2. Existing technologies and versions.
3. Existing implemented features.
4. Missing features.
5. Architecture conflicts.
6. Recommended module boundaries.
7. Database ERD proposal.
8. Organization and property ownership strategy.
9. SaaS and On-premise deployment strategy.
10. White-label branding data model.
11. Branding inheritance flow.
12. Subscription and license design.
13. Security and tenant-isolation risks.
14. Production-readiness gaps.
15. Prioritized implementation backlog.
16. Recommended first milestone.

Classify each feature as:

- Implemented
- Partially Implemented
- Planned
- Unknown / Not Verified

Do not perform broad code changes during the assessment.

Ask for approval before major technology substitutions, destructive operations, or irreversible changes.

The objective is to understand the actual repository before implementing the next increment.

---

# 28. Product Architecture Principles — Final Summary

StayFlow PMS must remain:

- One unified product.
- One shared codebase.
- Modular monolith by default.
- Hotel and apartment capable.
- SaaS and On-premise capable.
- Multi-tenant with strict data isolation.
- Thai and English capable.
- White-label configurable.
- Subscription and license aware.
- Secure and auditable.
- Financially consistent.
- Operationally maintainable.
- Production-ready only after verified release gates.

The platform identity remains StayFlow PMS by default.

Customer branding may change the appearance and displayed product name, but must never change internal technical identifiers or compromise tenant isolation.

Build a reliable commercial PMS first. Avoid premature complexity and prioritize correctness, security, and incremental delivery.

---

# 29. Channel Manager Integration Architecture

## 29.1 Design principle

StayFlow PMS connects to OTAs through a channel manager intermediary,
not through direct OTA APIs. One channel manager integration connects
to hundreds of OTAs.

## 29.2 Domain entities

### ChannelConnection

Represents a hotel's active connection to a channel manager provider.

Fields:
- Id
- OrganizationId
- PropertyId
- Provider (enum: STAAH, SiteMinder, Roomcloud, etc.)
- ProviderPropertyId (the hotel's ID in the channel manager's system)
- ApiCredentials (encrypted; never logged or exposed in API responses)
- IsActive
- LastSyncAtUtc
- CreatedAt / UpdatedAt

### ChannelRateMapping

Maps a PMS RoomType to a channel manager rate plan.

Fields:
- Id
- OrganizationId
- PropertyId
- ChannelConnectionId
- RoomTypeId (FK to RoomType)
- ChannelRoomTypeCode (channel manager's identifier)
- ChannelRatePlanCode
- IsActive

### ChannelReservation

Tracks inbound bookings received from the channel manager.

Fields:
- Id
- OrganizationId
- PropertyId
- ChannelConnectionId
- ChannelBookingReference (OTA's confirmation number)
- OtaSource (enum: Agoda, BookingCom, Traveloka, TripCom, Expedia, etc.)
- ReservationId (FK to Reservation, set after booking is created in PMS)
- RawPayload (JSON snapshot of the original webhook body, for audit)
- ProcessingStatus (Pending, Created, Duplicate, Failed)
- ReceivedAtUtc
- ProcessedAtUtc
- FailureReason

## 29.3 Outbound sync (PMS → Channel Manager)

Trigger: availability changes when a reservation is confirmed,
cancelled, or a room is taken out of service.

Responsibilities:
- Calculate available room count per room type per date.
- Push updated availability to the channel manager via their API.
- Record the sync result (success / failure / retry).
- Do not block the PMS operation if the channel manager is unavailable;
  queue the sync and retry with exponential backoff.

## 29.4 Inbound webhook (Channel Manager → PMS)

Trigger: channel manager sends an HTTP POST to StayFlow when a guest
books on an OTA.

Responsibilities:
- Authenticate the webhook (HMAC signature or shared secret).
- Parse the booking payload (channel-manager-specific format).
- Create a ChannelReservation record immediately (idempotent on
  ChannelBookingReference).
- Run the same availability and conflict check as a direct booking.
- Create a Reservation in the PMS and link it to the ChannelReservation.
- If the room is no longer available, reject the booking and notify the
  channel manager.
- Record the full raw payload for audit.

## 29.5 Provider abstraction

Define an interface: `IChannelManagerProvider`

Methods:
- `PushAvailabilityAsync(propertyId, roomTypeId, dates, count)`
- `PushRatesAsync(propertyId, roomTypeId, ratePlanCode, dates, rate)`
- `ParseInboundBooking(rawPayload) → ChannelBookingDto`
- `ValidateWebhookSignature(request, secret) → bool`

Implement one concrete class per provider:
- `StaahChannelManagerProvider`
- `SiteMinder ChannelManagerProvider`

Register providers in DI keyed by `ChannelProvider` enum.
Never scatter provider-specific logic outside the provider class.

## 29.6 API routes

```
GET    /api/v1/properties/{propertyId}/channel-connections
POST   /api/v1/properties/{propertyId}/channel-connections
PUT    /api/v1/properties/{propertyId}/channel-connections/{id}
DELETE /api/v1/properties/{propertyId}/channel-connections/{id}

GET    /api/v1/properties/{propertyId}/channel-connections/{id}/rate-mappings
POST   /api/v1/properties/{propertyId}/channel-connections/{id}/rate-mappings
DELETE /api/v1/properties/{propertyId}/channel-connections/{id}/rate-mappings/{mappingId}

POST   /api/v1/webhooks/channel/{connectionId}  ← inbound OTA bookings (public, HMAC-auth)
POST   /api/v1/properties/{propertyId}/channel-connections/{id}/sync  ← manual sync trigger
```

## 29.7 Security requirements

- Never store API credentials in plaintext; encrypt at rest.
- Never log API credentials or webhook secrets.
- Validate webhook HMAC signatures before processing payloads.
- Treat the webhook endpoint as public but authenticated by signature.
- Apply rate limiting to the webhook endpoint.
- Sandbox credentials and production credentials must be clearly
  separated in configuration.
- Audit every inbound booking and every outbound sync attempt.

## 29.8 Acceptance criteria

- A hotel can configure a STAAH connection with their property ID and API key.
- Room types can be mapped to STAAH rate plans.
- When a reservation is confirmed in the PMS, availability is pushed to STAAH.
- When STAAH sends a booking webhook, a PMS reservation is created automatically.
- Duplicate webhook deliveries do not create duplicate reservations.
- If the channel manager is unreachable, the PMS continues to operate normally.
- Channel credentials are never returned in API responses.
- Inbound bookings with no available room are rejected cleanly.
- All sync attempts are recorded with status and timestamp.