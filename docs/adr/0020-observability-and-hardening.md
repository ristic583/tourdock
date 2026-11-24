# ADR 0020: Observability and hardening

- **Status:** Accepted
- **Date:** 2026-11-08

## Context
Before going further, the API needed three things:
- **Logs** someone can search, with no personal data in them.
- **A way to follow one request** across log lines and services.
- **Protection against abuse:** floods, brute force, oversized requests and clickjacking.

Two real problems turned up:
- **Rate limits counted everyone together.** All browser traffic reaches the API through the web app's server, so the
  per-IP sign-in limit treated every visitor as one client. A few mistyped passwords could lock everyone out for a minute.
- **The logs were mostly SQL.** Npgsql logged every SQL statement at Information level, burying real events.

## Decision

**Logs**
- **Format:** JSON lines on stdout in containers, using .NET's built-in JSON console (no Serilog). Development keeps
  readable text.
- **Trace ids:** every line carries `TraceId` and `SpanId`.
- **Noise:** Npgsql and EF Core log at Warning. Health probes are not logged.
- **Requests:** one line each, with method, path, status and duration. Headers, query strings and bodies are never
  logged: they can carry tokens, search terms and personal data.

**Following a request.** W3C `traceparent` end to end:
- The web server forwards the browser's trace context, or starts one, on every BFF call.
- The API uses it for its request, so all log lines of that request share the trace id.
- Every error response includes `traceId`, so a user who quotes it leads straight to the logs.

**Traces and metrics: OpenTelemetry.**
- **Instrumented:** ASP.NET Core, outgoing HTTP (payments, rates, weather), Npgsql and the .NET runtime.
- **Business counters** (`TourDock` meter): bookings created, confirmed, cancelled (by who) and completed; refunds;
  reviews; failed payments; emails by kind and delivery result; outbox backlog and parked messages. Booking and review
  counters are driven by domain events in the same place that writes them to the outbox (ADR 0018), so no code path is
  missed.
- **Export is opt-in** via `OTEL_EXPORTER_OTLP_ENDPOINT`. `docker compose --profile observability up` adds the Aspire
  Dashboard locally for traces, metrics and structured logs.

**Health.** `/health/ready` now includes the outbox:
- It reports **Degraded** when messages gave up in the last 7 days, or the oldest pending one waited over 10 minutes.
- Degraded still answers 200: late emails are worth a look, not a reason to take the API out of service.
- Liveness stays dependency-free.

**No personal data in logs.**
- Every log statement was reviewed. One leaked: the admin seeder logged ASP.NET Identity's error descriptions, which can
  repeat the email address. It now logs only the error codes.
- An integration test runs registration, a failed login, booking, payment, cancellation and the resulting emails, then
  fails if any email address, name, password or token appears in the captured logs.

**Client IP and rate limits.**
- **Forwarded IP:** `X-Forwarded-For` is accepted only from the web container. The compose network has a fixed address
  range, and the web container a fixed IP, which is the only trusted proxy. Anyone else sending the header is ignored,
  so the header can't be used to dodge limits.
- **Limits:**
  - per IP overall: 100 requests per 10 s (stops floods)
  - per IP on sign-in: 10 per minute
  - per user on bookings and payment starts: 10 per minute
  - per user on reviews and reports: 5 per minute
- **When exceeded:** a 429 ProblemDetails with `Retry-After` and code `RateLimit.Exceeded`. Health probes are never
  limited.
- **Configurable:** all limits are settings (`RateLimiting:*`).

**Response headers and request limits.**
- **Headers:** `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, and
  `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`. Nothing in an API response may load or be
  framed. The dev-only interactive docs are exempt from the CSP.
- **Caching:** responses to signed-in requests are `Cache-Control: no-store`.
- **CORS:** none. Browsers never call the API directly.
- **Request bodies:** limited to 1 MB; photo uploads keep their own 10 MB limit.
- **Timeouts:** requests time out after 30 s (504).

## Consequences
- ✅ A support request with a `traceId` maps to the exact log lines, and the logs stay readable and free of personal data.
- ✅ The sign-in limit protects each visitor separately. Spoofed `X-Forwarded-For` headers are ignored.
- ⚠️ Rate limits are kept per instance in memory: with N API instances the effective limit is N×. A shared store
  (Redis) or limits at the reverse proxy would make them global.
- ⚠️ Server-rendered pages don't forward a trace context yet (only BFF calls do). Next.js' own OpenTelemetry
  integration would close that gap.
- ⚠️ The web app sends its own baseline headers but no Content-Security-Policy yet. With inline scripts and per-request
  nonces in Next.js, that's a project of its own.
- ⚠️ No alerting: metrics and Degraded health are visible, but nothing pages anyone yet.
- ⚠️ The background jobs' database queries show up as many small standalone traces (the outbox polls every 2 s). A
  sampler that drops parentless database spans, or tracing the jobs as their own spans, would tidy that up.
