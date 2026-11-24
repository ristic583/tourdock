# Conventions

Binding rules for TourDock, taken from what the code already does well. If you need to break a rule, record why in an ADR.
Known violations are tracked separately. They are not exceptions to these rules.

## 1. Layers and placement

| Rule | Example |
|---|---|
| L1. Dependencies point inward: Api → Application → Domain; Infrastructure → Application. Domain references nothing. | `tests/TourDock.ArchitectureTests` |
| L2. Business invariants live in aggregate methods or domain services and return `Result`. | `Slot.ReserveSeats`, `BookingService.Cancel` |
| L3. Handlers orchestrate: authorize → load → call the domain → save. They hold no business rules. | `CancelBookingHandler` |
| L4. Endpoints bind the request, call one handler and map with `ToHttp()`. They contain no conditions on business data. | `AgencyEndpoints.cs` |
| L5. Repository interfaces live in `Application/Abstractions/Data`; EF Core stays in Infrastructure. Raw SQL only through `FromSql`/`ExecuteUpdate` with parameters. | `Repositories.cs:76` |
| L6. One feature folder per area in Application (`Bookings/`, `Reviews/`…). One file per use case, named after it, holding the command or query, its validator and its handler; an outbox message sits with its handler. Shared responses and errors go to `<Feature>Contracts.cs`, shared helpers to their own file. | `Bookings/CreateBooking.cs`, `Bookings/BookingContracts.cs` |
| L7. Shared multi-handler flows are one small class (`BookingChanger`, `TourEditor`), not a base class. | — |

## 2. Naming

- Handlers: `<Verb><Noun>Handler`. Commands and queries: `<Verb><Noun>Command` / `Query`. A handler's parameter is named `command` or `query`. Responses: `<Noun>Response`; DTOs: `<Noun>Dto`.
- Error codes: `"<Area>.<Reason>"` (`"Booking.NotFound"`), declared as `static readonly Error` on an `*Errors` class: `<Aggregate>Errors` in
  the domain (`BookingErrors`), `<Feature>UseCaseErrors` in Application when the domain already has that name, else `<Feature>Errors`.
- Tests read as sentences: `Only_the_traveler_can_review_and_only_after_the_trip`.
- Web: files are kebab-case, components PascalCase, hooks `useX`.

## 3. Validation and errors

| Kind of check | Where | How |
|---|---|---|
| Shape (required, length, range, format) | FluentValidation validator next to the command | Runs in the validation decorator, which returns 400 ProblemDetails with field errors |
| Business rule (seats left, status transition, policy) | Domain | `Result` with `Error.Conflict/Validation` |
| Access (who may do this) | Agency requests: `IAgencyRequest` + access decorator, **before validation** (outsiders get 404 whatever they send); the handler checks again. Own data: owner comparison in the handler | `AgencyAccessDecorators.cs`, `IAgencyAccess.RequireAsync` |
| HTTP binding (missing header, multipart, size) | Endpoint | Only things that a command cannot express |

- No exceptions for expected outcomes. Throw only when an invariant is broken by a programming error.
- Every error response is ProblemDetails with `code` and `traceId`.
- "Not yours" and "doesn't exist" give the same answer: 404, never 403, so a caller can't tell which ids exist.

## 4. Domain invariants (time, money, ordering, concurrency)

- T1. Instants are `DateTimeOffset` in UTC. Schedules are local time plus an IANA zone. Convert at the edge (`TimeZoneInfo.ConvertTime`).
- T2. Time ranges are half-open `[start, end)`. Stored windows use `tstzrange` / `daterange` with exclusion constraints where overlap is forbidden.
- T3. "Now" comes from `TimeProvider`, never `DateTime.UtcNow`.
- M1. Money is `Money` (decimal + currency). Never use `double`. Refunds never exceed paid minus refunded; enforce this in the domain and as a DB backstop.
- C1. A change to departure capacity takes the row lock (`GetByIdForUpdateAsync`) or relies on the `xmin` version plus `ConcurrencyRetry`. Never check-then-write without one of the two.
- C2. Any uniqueness the business needs is a unique index, not only a check in code (idempotency key, one pending payment, one review per booking).
- C3. A batch job saves each item on its own, so one conflict never blocks the rest.
- O1. Lists have a deterministic order: a business key, then `Id` as tiebreaker.

## 5. Persistence and messaging

- P1. A side effect outside the database (email, refund, file delete) is an outbox message written in the same transaction. Handlers must be idempotent: check state first, use our id as the provider idempotency key.
- P2. Incoming events are de-duplicated by id in the same transaction as their effect. A duplicate returns 200.
- P3. A provider call made before commit is allowed only when it is idempotent on our id (`StartPaymentHandler`).
- P4. Every terminal failure (a parked outbox message, a failed refund) is visible as a metric, in the readiness check (`Degraded`) and in the admin UI.
- P5. Migrations are EF migrations. SQL that EF can't express goes in `migrationBuilder.Sql` with a comment.
- P6. Soft delete through a query filter for tours and vehicles. Bookings, payments and refunds are never deleted.

## 6. Security

- S1. Every agency-scoped read calls `RequireAsync(agencyId, Read)` and loads through a repository method that takes `agencyId`.
- S2. Every write calls `RequireAsync(..., Manage)` or compares the owner. Admin endpoints sit in a group with `RequireAuthorization(AuthPolicies.Admin)`.
- S3. Every endpoint states its auth explicitly: `RequireAuthorization(...)` or `AllowAnonymous()`. Anonymous reads apply the published / active-agency filter.
- S4. A user-supplied URL is never fetched by the server. Redirect targets go through an exact-origin allow-list (`ReturnUrlPolicy`). URLs from configuration point only at fixed providers.
- S5. Client IP comes from `X-Forwarded-For` only when the immediate peer is a configured trusted proxy; otherwise use the socket address. This applies to the API (`ForwardedHeaders:KnownProxies`) and to the web edge (`TRUSTED_PROXIES`).
- S6. Secrets come only from env vars or User Secrets. The deploy overlay marks each one `:?required`, and Production refuses to start on a `local-dev-only*` value.
- S7. Tokens live only in httpOnly cookies, `SameSite=Lax`, `Secure` whenever the public origin is https. The BFF blocks `/auth` and `/webhooks` and refuses cross-site writes.
- S8. Logs never contain emails, names, passwords, tokens or query strings (`PrivacyTests`).
- S9. Each costly or abusable endpoint has a named rate-limit policy (auth per IP, writes and emails per user).
- S10. Uploads are decoded and re-encoded server-side, with size and dimension limits. Client names and types are ignored.

## 7. Size limits

| Limit | Value | Exempt |
|---|---|---|
| File | ≤ 300 lines | generated (`schema.d.ts`, migrations), seed data (`DemoCatalog.cs`) |
| Method / function / component | ≤ 60 lines | endpoint group registration ≤ 80 |
| Line | ≤ 140 characters | generated, seed data |

Enforced automatically: C# by `tests/TourDock.ArchitectureTests/SizeLimitTests.cs`, web by ESLint (`max-len`, `max-lines`,
`max-lines-per-function`). Files that were already over the limits are listed in `size-baseline.txt` (C#, may not get worse)
and `web/eslint.size-baseline.mjs` (warnings only). Both lists only shrink. Layer and placement rules (§1) are enforced by
`LayerDependencyTests` and `PlacementTests`, and explicit endpoint auth (S3) by `EndpointAuthTests`.

## 8. Frontend

- F1. Client components fetch data through a per-feature module (`lib/api/*-client.ts`, `lib/<feature>.ts`), not `browserApi` directly. Server components and pages may call `serverApi`.
- F2. Every data view handles loading, empty, error (ProblemDetails `title` and `traceId`) and forbidden or not-found states.
- F3. Every page works at 360 px width. Forms are usable on touch.
- F4. API types come from the generated `schema.d.ts` (`npm run api:generate`), never hand-written copies. No `any`.
- F5. A destructive action asks for confirmation and shows a pending state.

## 9. Tests

- Each bug or security fix gets one regression test that fails before the fix.
- Each sensitive agency read gets one foreign-agency test; each sensitive write gets one forbidden-role test.
- Integration tests run against real PostgreSQL (Testcontainers), never EF InMemory.
- Run per change: unit tests plus the touched integration class. Run the full suite at milestones (see the global test rules).
- No e2e test unless the behaviour is only observable in the UI.

## 10. Docs

- An architectural or product decision gets an ADR (`docs/adr/NNNN-*.md`, English, no private data).
- Keep the README's run, test and test-count sections current in the same PR that changes them.
- No real names (other than the author), emails, hosts or secrets in the repo. Seed data uses `example.com`.
