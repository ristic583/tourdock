# ADR 0009: Public search and caching

- **Status:** Accepted
- **Date:** 2026-11-06

## Decision
**Read side.** Public endpoints (`GET /api/v1/tours`, `/tours/{id}`, `/tours/{id}/departures`) are anonymous and go through
`ITourCatalog`. It runs no-tracking EF Core projections straight to DTOs, separate from the write-side aggregates (light CQRS).

**What travelers see:** published tours of **active** agencies that have at least one departure in the next **365 days** (90 originally; see ADR 0008)
that can take the group:
- **Shared:** enough remaining seats, and the departure isn't privately booked.
- **Private:** nothing reserved yet, and capacity is at least the group size.
- With a `date`, the departure must be on that **local** date at the tour's location.

**Filters and sorting**
- **Filters:** city (prefix, case-insensitive), country, date, guests, price range (base price of the requested mode),
  and mode (`Shared` / `Private`).
- **Sorting:** soonest departure (default), price ascending or descending, rating.
- **Pagination:** offset (`page`, `pageSize` ≤ 50), with `totalCount` and `totalPages`. Every sort ends with the id, so pages are stable.

**Indexes**
- A GIN **trigram** index on `tours.city`: `ILIKE 'spl%'` uses `Bitmap Index Scan on ix_tours_city_trgm`, verified with `EXPLAIN`.
- A partial index on open departures by `(local_date, tour_id)` for date searches.
- The existing `(tour_id, starts_at)` index for "next departure".

**Caching:** .NET `HybridCache` for search results and tour pages.
- **30-second TTL.** Seat counts change with every booking, so entries stay short-lived. The booking flow always re-checks
  availability inside a transaction, so a stale search result can never cause an overbooking.
- **Tag-based invalidation.** Commands that change what travelers see (publishing, prices, schedules, cancelling departures,
  approving or suspending agencies) implement `IInvalidatesCatalog`. A command decorator clears the `catalog` tag after they succeed.
- **Stampede protection.** HybridCache coalesces concurrent misses for the same key into one database query.
- **L1 / L2.** In-memory today. Registering a distributed cache (Redis) adds a shared L2 with no code changes. That's needed
  once there are several API instances, because in-memory invalidation only clears the local instance.

**Demo data.** With `Seed__DemoData=true` (the compose default), an empty database is filled with fictional agencies,
vehicles and tours, so search works right after `docker compose up`.

## Consequences
- ✅ Fast, index-backed queries. Changes show up immediately. Cache misses can't stampede the database.
- ⚠️ Offset pagination gets slower on very deep pages. Keyset pagination would fix that at scale.
- ⚠️ City search is a prefix match on one column. A dedicated search engine (OpenSearch/Meilisearch) would add full-text,
  typo tolerance, facets and geo-distance. PostGIS could handle "near me" inside Postgres.
