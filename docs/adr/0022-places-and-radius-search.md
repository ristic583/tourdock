# ADR 0022: Places, autocomplete and radius search

- **Status:** Accepted
- **Date:** 2026-11-09

## Context
- Location search was a prefix match on the tour's city text (`city ILIKE 'split%'`). A tour in Solin, 6 km from Split,
  didn't show up for "Split". "Sibenik" didn't find Šibenik. Yet the page said "tours near Split".
- Every tour already stores the latitude and longitude of its meeting point, but they were only used for the weather.
- Agencies typed coordinates by hand, and new tours silently defaulted to Split's.

## Decision

**Seeded places**
- A `cities` table with about 130 cities and towns in Croatia, Montenegro, Slovenia, Bosnia and Herzegovina and
  Albania: name, country, coordinates, population and time zone. It's reference data, seeded by a migration with
  stable UUIDv5 ids.
- `search_name` is the name in lower case without accents ("Šibenik" → "sibenik"), with a btree `text_pattern_ops`
  index, so a prefix `LIKE` uses it.
- The folding is an explicit table (`PlaceSearch.Normalize`). The API runs in invariant-globalization mode, where
  Unicode decomposition isn't available; a unit test caught that.
- Update (2026-11-10): about 45 places in Serbia and North Macedonia were added by a second migration. Serbian and
  Macedonian Cyrillic is transliterated by the same table ("Ниш" finds Niš), and an optional `search_alias` (its own
  partial `text_pattern_ops` index) holds a second name, so "Belgrade" finds Beograd.

- Update (2026-11-14): the search box also suggests **countries**. `GET /api/v1/places?q=ser` returns matching
  countries (English or local name, Cyrillic folded the same way: "Srbija", "Србија") with up to six of their places
  nested underneath (most tours nearby first), followed by the usual place matches. Picking a country searches
  `GET /tours?country=RS`; a whole country name typed without picking does the same. The country list is a short
  static list in code (`Countries`), since it only changes when a migration adds places in a new country. The web shows
  flags as emoji (regional-indicator letters), so no images or extra requests; Windows draws them as the two letters.
  `GET /cities` stays places-only for the agency's tour form, where a country isn't a valid pick.

**Autocomplete:** `GET /api/v1/cities?q=sib`
- Places with published tours within 25 km come first (`toursNearby`), then larger places.
- Responses are cached for 5 minutes (HybridCache). They're cleared with the catalog when agencies publish.

**Radius search in PostgreSQL**
- Uses the `cube` and `earthdistance` extensions, with a GiST index on `ll_to_earth(latitude, longitude)`.
- Two SQL functions, `td_within_radius` and `td_distance_m`, are mapped as EF Core DB functions. The LINQ query stays
  LINQ, and because they're plain `LANGUAGE sql IMMUTABLE` functions, PostgreSQL inlines them: `earth_box(...) @>
  ll_to_earth(...)` meets the index, and only rows inside the box get the exact distance check. An integration test
  checks the plan uses `ix_tours_location_earth`.
- `GET /tours?cityId=…&radiusKm=25` (1–100 km) filters by distance, fills `distanceKm` and allows `sort=Nearest`.
  Typed text that names a known place searches around that place too. Text that names no place falls back to the old
  prefix match on the tour's city.

**Web**
- The search box is an accessible combobox; the results page shows "within 25 km", the distance on each card, a
  radius chooser and "Nearest first" by default.
- In the tour form, picking the city fills the country, time zone and coordinates. The agency then drops the pin on
  an OpenStreetMap map (Leaflet, loaded in the browser only). A new tour has no point until a city is picked.

## Alternatives
- **PostGIS:** the standard for geodata, but it needs a different database image and is more than radius search over
  points needs. Moving later is mostly swapping the two SQL functions.
- **Bounding box and Haversine in plain SQL:** no extensions, but more SQL to own, and a btree index only covers one
  axis well.
- **Geocoding street addresses** (Nominatim and similar): rate-limited third-party calls and a usage policy to follow.
  A city plus a pin gives the exact spot without them.

## Consequences
- The place list is curated, not complete. Importing GeoNames (CC BY 4.0, with attribution) is the next step for more
  countries.
- The map loads tiles from tile.openstreetmap.org. Fine for a demo; production would use a tile provider with an
  agreement, or self-host tiles.
