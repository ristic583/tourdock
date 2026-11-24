# ADR 0016: Agency stats endpoint

- **Status:** Accepted
- **Date:** 2026-11-06

## Context
The back-office overview showed KPIs that the browser computed from two range listings (`/slots` for 30 days and
`/bookings` for about two months) on every page view. That moves a lot of rows to compute a handful of numbers, and the
definitions lived in TypeScript where other clients can't reuse them.

## Decision
`GET /api/v1/agencies/{agencyId}/stats?from=&to=` returns the overview numbers, computed with aggregate queries in
PostgreSQL. Access is the same as other read endpoints: agency members, and admins read-only; everyone else gets `404`.

Definitions (`from`/`to` are **local departure dates** at each tour's location, inclusive, at most 366 days):
- **departures / vehiclesInUse / seatsOnSale:** open departures in the range, their distinct vehicles, their capacity.
- **seatsSold:** seats taken on those departures, including holds; a private booking takes the whole capacity.
  `occupancyPercent` = sold / on sale, rounded.
- **revenue:** `paid`, `refunded` and `net` (paid minus refunded) for bookings whose departure is in the range,
  in EUR (the only platform currency, ADR 0006).
- **today:** open departures on each tour's *local* today and the guests of their confirmed or completed bookings.
  It ignores the range, so one call serves both "this month" and "today".
- **pendingHolds:** bookings on hold right now (not yet expired), any departure date.

The UTC range is padded by a day on each side so the existing `(agency_id, starts_at)` indexes narrow the rows, then
filtered by local date. No new indexes or migrations.

## Consequences
- ✅ One small request for the overview, with definitions owned and tested by the API.
- ⚠️ Not cached: numbers are exact but each call runs about six aggregate queries. Fine for a back office; cache per
  agency for a few seconds if it ever shows up in profiles.
