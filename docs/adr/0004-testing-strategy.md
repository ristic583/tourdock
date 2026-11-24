# ADR 0004: Testing strategy

- **Status:** Accepted
- **Date:** 2026-11-05

## Decision
| Project | Scope | Dependencies |
|---|---|---|
| `TourDock.Domain.UnitTests` | Invariants, value objects. The most valuable unit tests. | None (pure C#) |
| `TourDock.Application.UnitTests` | Handler orchestration | Fakes and NSubstitute |
| `TourDock.Api.IntegrationTests` | Full HTTP stack: routing, auth, validation, error format, EF mapping, DB constraints, concurrency | `WebApplicationFactory` + **real PostgreSQL via Testcontainers** |
| `TourDock.ArchitectureTests` | Enforce the dependency rule from ADR 0001 | NetArchTest |

- Tooling: **xUnit v3** on Microsoft.Testing.Platform, **AwesomeAssertions** (Apache-2.0 fork of FluentAssertions v7),
  **NSubstitute**. All are free for commercial use. FluentAssertions v8+ and Moq were avoided because of licensing and trust concerns.
- We never use the EF Core InMemory provider. It doesn't enforce constraints, transactions or concurrency,
  which are exactly the things that matter most in a booking system.

## Consequences
- ✅ Tests exercise the real database behaviour that business rules depend on.
- ⚠️ Integration tests need Docker and take a few seconds to start a container. One container is shared per test class fixture.
