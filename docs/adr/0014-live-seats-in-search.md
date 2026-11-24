# ADR 0014: Live seat counts in search results

- **Status:** Accepted
- **Date:** 2026-11-06

## Context
Search cards show "N of M seats left" for the next departure. `TourSummary` only had `nextDepartureAt`, so the web made
one `/departures` call per card (up to 12 per page). Search results are cached for 30 seconds (ADR 0009), and bookings do
not clear the cache, so seat counts inside a cached result could be up to 30 seconds old.

Clearing the `catalog` tag on every hold, payment, cancellation and expiry would keep counts fresh, but it would also
throw away every cached search and tour page on every booking, which defeats the cache exactly when traffic is highest.

## Decision
- **New field:** each `TourSummary` has `nextDeparture { id, startsAt, localStart, remainingSeats, capacity }`, the
  soonest departure that can take the searched group (same rules as the search filter: date, guests, mode, 365-day
  horizon). `nextDepartureAt` stays and always equals `nextDeparture.startsAt`. `timeZoneId` is added so clients can
  format local times.
- **Cache the match, not the seats.** HybridCache still holds *which* tours match and their order. After every lookup
  (hit or miss), one query reads the next bookable departure of the tours on that page straight from the database
  (`GROUP BY tour_id` + `ROW_NUMBER`, at most `pageSize` tours, served by the `(tour_id, starts_at)` index).
- **Sold out since cached:** the tour stays on the cached page with `nextDeparture: null` until the entry expires.
  Clients should render that as "sold out for this date / group".
- Bookings still don't invalidate the catalog. Agency changes do, as before.

## Consequences
- ✅ Seat counts in search are never stale, with no invalidation storm on bookings. The web drops its per-card calls.
- ✅ The extra query is small and bounded by the page size.
- ⚠️ Every search request touches the database once, even on a cache hit. Fine at this scale; with a shared L2 cache and
  many instances, a short-lived per-tour seat cache could be layered on top.
- ⚠️ Membership and ordering can still be up to 30 seconds old (a tour may appear with `nextDeparture: null`).
