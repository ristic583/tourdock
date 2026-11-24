# End-to-end tests

Playwright tests for whole journeys, run against the Docker Compose stack (demo data on).

```bash
# from the repo root, with the stack up (any project name, here "tdweb" with PAYMENTS_PORT=18090)
docker run --rm --network tdweb_default -v "$PWD/web":/app -v /app/node_modules -w /app \
  -e E2E_FORWARD="3000=web:3000,18090=payments:8080" -e E2E_MAILPIT_URL=http://mailpit:8025 \
  mcr.microsoft.com/playwright:v1.65.0-noble sh -c "npm ci && npx playwright test"
```

`a11y.spec.ts` runs axe-core (WCAG 2.1 A/AA) on the public pages in light and dark mode, and the journeys check the
checkout, "My bookings", the tour editor and the agency calendar too. Serious and critical findings fail the run.

New accounts confirm their email from the message in Mailpit (`E2E_MAILPIT_URL`, default `http://localhost:8025`).

The agency flow (`agency.spec.ts`) also needs an admin: start the stack with `ADMIN_EMAIL` / `ADMIN_PASSWORD` set and
pass the same values as `E2E_ADMIN_EMAIL` / `E2E_ADMIN_PASSWORD`. Without them that test is skipped.

`E2E_FORWARD` makes the app reachable as `localhost:3000` (it sets Secure cookies) and the payment sandbox at the
public URL the API hands out (`localhost:$PAYMENTS_PORT`). Without it, tests use `E2E_BASE_URL` (default
`http://localhost:3000`) directly.

The API limits auth requests to 10 per minute per IP, and all BFF traffic shares one IP. Restart the `api` container
between heavy runs if sign-ups start failing with 429.

`deploy/smoke-edge.sh [base-url]` checks that a client can't dodge the per-IP rate limits by faking
`X-Forwarded-For` (ADR 0024). Run it with the other release checks.
