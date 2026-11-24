# ADR 0025: Semantic search with embeddings

- **Status:** Accepted
- **Date:** 2026-11-24

## Context

Search filtered by place, date, group size, price and booking type (ADR 0009, 0022), and ordered by date, price, rating
or distance. A traveler who knows *what kind* of trip they want ("a calm boat trip for a family with small kids") had to
read every card. Tour descriptions say it in other words ("a gentle cruise, perfect with children"), so keyword search
wouldn't help much either.

## Decision

**Free text orders, filters decide.** `GET /api/v1/tours?q=...` (at most 200 characters) keeps every existing filter.
The filters decide *which* tours match; closeness in meaning to `q` decides their *order* (`sort=Relevance`, the
default when `q` is given). An explicit sort (price, rating, nearest) still wins. The response says what happened:
`semantic` is `null` without `q`, `true` when `q` ordered the results, `false` when semantic search was unavailable.

**Embeddings from Voyage AI.** `voyage-4`, 1024 dimensions, with `input_type` `document` for tours and `query` for
search text (retrieval models embed the two slightly differently). Plain HTTP with the same resilience setup as the
other providers (timeouts, retries, circuit breaker). The base URL is configuration: platform keys work only with
`api.voyageai.com`, MongoDB Atlas model keys only with `ai.mongodb.com`. Without a key the feature is off.

**What is embedded.** Title, place, duration, booking type and description (`TourDocument`). Not price, dates or seats:
they change daily, SQL filters them exactly, and they would only make vectors stale.

**Stored in PostgreSQL with pgvector.** No second datastore to run, back up or keep consistent.
- The database image is built from `deploy/db/Dockerfile`: the same Alpine PostgreSQL 18 image as before, with pgvector
  compiled in (pinned version and commit). The ready-made `pgvector/pgvector` images are Debian-based; switching an existing
  data volume from musl to glibc changes how text sorts under the indexes already on disk. Integration tests build the
  same Dockerfile.
- `tour_embeddings (tour_id, model, content_hash, embedding vector(1024), updated_at)` has no EF model. The sync job
  writes it with a parameterized upsert, casting a `real[]` parameter to `vector`; search reads it through
  `td_semantic_distance(tour_id, model, query)`, a SQL function mapped as an EF Core DB function, like the distance
  functions in ADR 0022. No .NET type has to understand `vector`.
- **No approximate index (HNSW) for now.** Search filters first and ranks a few hundred tours at most; an exact scan over
  them is fast and always correct. HNSW with filters can return fewer rows than asked for unless iterative scans are
  tuned. Worth revisiting at tens of thousands of tours, or for an unfiltered "similar tours" feature.

**Kept in sync by reconciliation, not per edit.** A background job (every 60 seconds) hashes each published tour's text
together with the model and embeds only tours whose hash differs from the stored one, 32 per request.
- Tour commands don't know about embeddings, and there's no outbox message to lose or park.
- A provider outage, a new model or a changed `TourDocument` heals itself on the next run; seeded and old tours need no
  backfill step.
- Batches keep the number of requests low, which matters under free-tier rate limits.
- Trade-off: an edit is reflected within about a minute, and a just-published tour has no distance yet, so it sorts
  after the ranked ones until the next run.

**Search never fails because of the provider.** A query vector is fetched with a 2.5-second budget and cached for a day,
keyed by a hash of the normalized text (trimmed, single spaces, lower case). On timeout or error the search runs in its
normal order with `semantic: false`. The search-result cache key includes the normalized text and whether ranking by
meaning was applied.

**Privacy.** Search text is never logged (only the failure type is), and cache keys hold a hash of it. Tour texts are
public. The search text goes to Voyage AI, which the privacy page says.

## Cost limits (added 2026-11-24)

Voyage has no spending cap, so the API keeps two limits of its own:

- **Token budget.** Every Voyage call is counted with the tokens Voyage reports (`embedding_token_usage`, one atomic
  upsert per call). Once a model's count reaches `VOYAGE_TOKEN_BUDGET` (default 150 million, below the 200 million free
  tokens of a new account), no more calls are made: searches fall back to the normal order and the sync job waits. The
  count lives in the database, so restarts and deploys don't reset it. The same key used elsewhere (local runs, evals)
  isn't counted, hence the margin.
- **Per visitor.** A search text that needs a new vector takes a permit from the visitor's share (by IP): 20 a minute
  and 200 a day by default. Cached texts are free. Over the share, search still answers in its normal order
  (`semantic: false`). Searches made by the assistant and by MCP clients count against the caller's share. The windows
  are kept in memory, so a restart resets them.

## Ranking quality (measured 2026-11-24)

A one-off run of 25 searches written the way travelers ask (18 in English, 6 in Serbian, 1 in German) against the
38-tour demo catalog with `voyage-4`. The tours that genuinely fit were marked by hand for each search.

| | First result fits | A fitting tour in the top 3 | Precision@3 | MRR |
|---|---|---|---|---|
| `voyage-4` | 24/25 (96%) | 25/25 | 100% | 0.98 |
| Random order (what the date-order fallback amounts to) | 5% | 14% | | 0.16 |

The one miss put the Benagil cave above the Blue Cave for "glowing blue cave you enter by boat". Serbian and German
searches found English tour texts as well as English ones did. Three searches with no fitting tour (tuna fishing,
skydiving, a diving course) still returned the nearest boat trips: ranking has no relevance cut-off, so search never
says "nothing matches" (the assistant judges that itself). The catalog is small and its tours are distinct, so treat
these numbers as an upper bound for a larger, more uniform catalog.

## Consequences

- ✅ Travelers can describe a trip in their own words; filters keep the results correct (date, seats, price, place).
- ✅ One database; the vector feature degrades to the old behavior instead of breaking search.
- ✅ The sync is self-healing and needs no changes in the write side.
- ⚠️ The database image is now ours to build and publish (CI does it with the other images).
- ⚠️ Ranking quality depends on the model and on how well agencies describe their tours; it was measured once, on a
  small demo catalog (above), not continuously.
- ⚠️ A query costs one provider call per distinct text per day; a flood of unique texts is bounded per visitor, and
  in total by the token budget.
