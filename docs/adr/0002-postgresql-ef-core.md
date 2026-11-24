# ADR 0002: PostgreSQL with EF Core

- **Status:** Accepted
- **Date:** 2026-11-05

## Context
Core invariants include "a boat can't run two tours at the same time" and "a slot can't be overbooked".
The database should be able to enforce these, not just the application code.

## Decision
- **PostgreSQL**, running as its own Docker Compose service.
- **EF Core (Npgsql provider)** for all data access. For atomic or performance-critical operations
  we use `ExecuteUpdate` or raw SQL through EF (`FromSql`) rather than introducing a second data-access library.

## Rationale
- Range types (`tstzrange`) and **exclusion constraints** let the database reject overlapping time slots
  for the same resource. That guarantee holds even if application code has a bug or a race condition.
- No licensing cost in any environment.
- Native arm64 images and a small footprint for local development.

## Consequences
- ✅ Overlap invariants can be enforced at the database level.
- ⚠️ Some features (exclusion constraints, range types) are Postgres-specific, so switching databases would be costly. That's accepted.
- ⚠️ Integration tests must run against real PostgreSQL (Testcontainers). The EF Core InMemory provider doesn't enforce constraints or transactions.
