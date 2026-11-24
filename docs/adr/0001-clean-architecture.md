# ADR 0001: Clean Architecture with a lightweight handler abstraction

- **Status:** Accepted
- **Date:** 2026-11-05

## Context
TourDock has a domain with real rules: availability, pricing rules, a booking lifecycle, cancellations and refunds.
These rules have to stay correct as the product grows, and they have to be testable without infrastructure.

## Decision
- Four projects: `Domain`, `Application`, `Infrastructure`, `Api`.
  Dependencies only point inwards (`Api -> Infrastructure -> Application -> Domain`).
- **Domain** has no persistence or framework knowledge. Business invariants live on aggregates
  (for example `slot.Reserve(groupSize)`), not in handlers.
- **Application** holds use cases (commands and queries), input validation, repository interfaces and interfaces for external services.
  Handlers only orchestrate: load, call domain behaviour, save, map.
- **Infrastructure** implements those interfaces (EF Core, PostgreSQL, external HTTP APIs, storage).
- **Api** is the composition root plus thin HTTP endpoints.
- Use cases are dispatched through our own small `ICommandHandler` / `IQueryHandler` abstraction.
  Cross-cutting concerns (validation, logging, transactions) are DI decorators. We don't use MediatR.
- The dependency rule is enforced by architecture tests (`tests/TourDock.ArchitectureTests`).

## Consequences
- ✅ Business rules are isolated and easy to unit-test. Clear boundaries make it possible to work on several layers in parallel.
- ✅ No commercially licensed dispatch library, and no runtime reflection for dispatch.
- ⚠️ More ceremony: a simple feature touches several files (entity, command, handler, DTO, mapping).
- ⚠️ We own about 50 lines of decorator plumbing, and decorator order has to be chosen deliberately.
- Note: this structure helps code and team scalability. It doesn't affect runtime scalability, which comes from statelessness, caching and the database.
