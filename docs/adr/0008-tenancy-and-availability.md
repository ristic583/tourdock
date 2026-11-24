# ADR 0008: Tenant isolation and availability management

- **Status:** Accepted
- **Date:** 2026-11-06

## Tenant isolation
- Agency back-office routes are scoped to an agency: `/api/v1/agencies/{agencyId}/...`.
- Every use case first calls `IAgencyAccess.RequireAsync(agencyId, level)`:
  - **Read:** any member (manager or guide). Admins also get read-only support access.
  - **Manage:** managers only. A suspended agency can't make changes.
  - **Non-members get `404 Not Found`, not `403`**, so they can't even learn that the agency or resource exists.
- Every repository lookup of agency-owned data **filters by agency id** as well as the resource id.
  A tour id from agency A requested through agency B's route is "not found". There's no path where a forgotten check leaks another tenant's data.
- Agencies can prepare draft tours while waiting for approval. Only an **active** agency can publish.

## Availability
- **Schedule rules** ("every Saturday at 09:00, May to September, vehicle *Sea Breeze*") create departures for the next
  **365 days** as soon as they're added (it was 90 days until the demo catalog went multi-season: a summer trip has to be
  bookable in winter). A background job tops them up every 6 hours.
  - **Idempotent:** a unique index on `(schedule_rule_id, starts_at)` means re-running never duplicates.
    Running it on several instances is harmless.
  - Departures that would double-book the vehicle are **skipped and counted** (shown in the response).
    The exclusion constraint still guards against races.
- **Manual departures** take a **local** start time ("2027-06-01T09:00"), which is converted using the tour's time zone.
- **Block / unblock** a departure (only while nothing is reserved).
- **Cancel** a departure: all its active bookings are cancelled with a **full refund** in the same transaction, and the vehicle is freed.
- Deactivating a schedule rule stops future generation. Existing departures and bookings are kept.
- A vehicle with upcoming departures or active rules can't be deleted. A tour with upcoming active bookings can't be deleted.
- Changing prices or policy never affects existing bookings (price and policy snapshots, ADR 0006).

## Database conflicts → HTTP
When two requests race past the application checks, PostgreSQL decides:
- exclusion violation (`23P01`) → `409 Conflict`
- unique violation (`23505`) → `409 Conflict`
- optimistic concurrency conflict → `409 Conflict`

## Consequences
- ✅ Tenant isolation is enforced in two places: access checks and agency-scoped queries.
- ⚠️ The generation job loads all tours with active rules on each run. That's fine at this scale;
  at larger scale it would page through tours or be partitioned across workers.
