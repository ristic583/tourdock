# ADR 0017: External APIs for exchange rates and weather

- **Status:** Accepted
- **Date:** 2026-11-07

## Context
Travelers want to see prices in their own currency and the weather at the tour location on the departure day.
Both come from free public APIs we don't control: they can be slow, down, rate-limited or return garbage. Neither is
needed to book a tour, so an outage must never break a page or a booking.

## Decision

**Providers (both free, no API key, so no secrets to manage):**
- **Frankfurter**: ECB reference rates, published once per working day.
- **Open-Meteo**: hourly forecast for any coordinates, up to 16 days ahead. Free for non-commercial use, data under CC BY 4.0.

**Display only.** Converted prices are labelled approximate. Every booking, payment and refund stays in **EUR** (ADR 0006),
so a rate can never change what someone is charged.

**API**
- `GET /api/v1/exchange-rates` returns EUR rates for 10 European currencies plus USD. The web app converts prices on the
  client, so a search page needs one cached call instead of one call per tour.
- `GET /api/v1/tours/{id}?currency=USD` adds a `convertedPrices` block to the tour detail response. The cached tour stays
  in EUR and conversion happens per request, so the catalog cache isn't multiplied by currency. Rounding is to 2 decimals,
  away from zero, the same as `Money`. An unsupported currency returns `400`.
- `GET /api/v1/departures/{id}/weather` returns the forecast for the hour nearest to the departure at the tour's coordinates.
  The response carries a `status` so the client knows what to show: `Available`, `NotYetAvailable` (more than 14 days out;
  `forecastAvailableFrom` says when to ask again), `Past` or `Unavailable`.
- These endpoints answer **200 with a status flag**, never 5xx, when a provider is down.

**HTTP clients:** named `HttpClient`s from `IHttpClientFactory`, behind Application interfaces (`IExchangeRateService`,
`IWeatherService`). Base URLs and cache durations come from the Options pattern and are validated at startup.

**Resilience** (`Microsoft.Extensions.Http.Resilience`). The budget is tighter than for payments because these calls only
decorate a page:
- 3 s per attempt, 8 s in total
- 2 retries with exponential backoff and jitter (all calls are GETs, so retries are safe)
- a circuit breaker that opens at a 50 % failure rate over at least 5 calls in 30 s and stays open for 30 s. While it's open,
  calls fail fast instead of piling up on a dead provider.

**Caching** (`HybridCache`, in memory today; adding Redis turns on L2 without code changes):
- **Rates:** a fresh entry for 6 h plus a "last good" entry for 7 days. When the provider fails, the last good rates are
  served with `isStale = true` and their ECB date. With no rates at all, the client shows EUR only.
- **Weather:** one entry per place and UTC day, for 1 h (Open-Meteo updates roughly hourly). Coordinates are rounded to
  2 decimals (about 1 km, finer than the forecast grid), so every departure of a tour on a day shares one upstream call.
- `HybridCache` stampede protection means a cold cache under load still makes only one upstream call per key.

**Responses are validated, not trusted.** A wrong base currency, an empty or zero rate, or a non-JSON body is treated as a
failure and goes to the fallback. Hours with missing forecast values are skipped, not turned into zeros.

**No readiness health checks for these providers.** A third-party outage would otherwise take every API instance out of the
load balancer even though the API can still do its job. Failures are logged as warnings instead.

**Tests:** no test calls the real APIs.
- Application unit tests cover conversion and rounding, fallback to EUR only, the forecast horizon and the rough-conditions rule.
- Client tests run the real clients (resilience pipeline and cache) against a stub `HttpMessageHandler`: caching, retrying a
  transient error, stale fallback, implausible responses, the circuit opening, and picking the nearest hour.
- API tests run end to end with both providers stubbed.

## Consequences
- ✅ Pages stay fast and working when a provider is slow or down. At most one upstream call per key per cache period.
- ✅ Swapping a provider means one class in Infrastructure.
- ⚠️ The cache is per instance until Redis is added, so N instances make up to N upstream calls per period. That's fine for
  these volumes.
- ⚠️ A cold instance with the provider down shows EUR only until the provider recovers. A distributed cache, or persisting the
  last good rates in PostgreSQL, would fix that.
- ⚠️ Open-Meteo's free tier is non-commercial. A commercial deployment would need its paid plan or another provider, which is
  configuration plus one adapter.
- ⚠️ "Rough conditions" (wind ≥ 30 km/h, gusts ≥ 45 km/h or thunderstorm) is a generic hint, not a safety decision. Agencies
  still decide whether a departure runs (they can block or cancel it).
