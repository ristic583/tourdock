# ADR 0024: Edge proxy decides the client IP

- **Status:** Accepted
- **Date:** 2026-11-16

## Context
- The API's rate limits (sign-in, sign-up, global flood limit) are per client IP. Every API call comes from the web
  container, so the web app passes the visitor's IP in `X-Forwarded-For`, and the API trusts that header only from the
  web container (ADR 0020).
- The web app took that IP from the `X-Forwarded-For` the browser sent. With nothing in front of it that rewrites the
  header, a client could send a different fake address on every request and never hit a limit. Our own pentest
  confirmed it: 130 requests with random headers, 0 rate limited.
- Next.js route handlers can't see the connection's address when a header is present, so the web app can't fix this
  alone.

## Decision
- **A Caddy edge** (`deploy/Caddyfile`) is the only published entry to the web app. The `web` service is no longer
  published.
- The edge sets `X-Forwarded-For` to exactly one address:
  - the connection's address for everyone;
  - except for peers in `TRUSTED_PROXIES` (CIDRs), where it takes the right-most untrusted address from their header.
- Defaults: locally nobody is trusted. On the deployment `TRUSTED_PROXIES` is required and lists the reverse
  proxies in front of the edge, so public visitors keep their real IP. Clients reaching port 3000 directly are
  identified by their own address.

## Consequences
- One more container (~40 MB image, a few MB RAM). No change for the proxy in front: it still targets port 3000.
- A new reverse proxy in front (for example a load balancer) must be added to `TRUSTED_PROXIES`, or every visitor
  behind it shares one rate-limit budget.
- Regression check: `deploy/smoke-edge.sh` (fails if faked headers dodge the limits).
