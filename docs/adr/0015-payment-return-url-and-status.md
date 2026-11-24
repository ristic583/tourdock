# ADR 0015: Payment return URL and last payment status

- **Status:** Accepted
- **Date:** 2026-11-06

## Context
The payment sandbox (ADR 0011) had no way to send the traveler back after Pay or Decline, so the web opened checkout in
a new tab and polled the booking every 3 seconds. A declined payment left no trace a client could read: the booking just
stayed `Pending`.

A return URL supplied by the client is a classic **open redirect** risk: a phishing link could start a real TourDock
checkout that ends on an attacker's page.

## Decision
- **`POST /bookings/{id}/payments` takes an optional body `{ "returnUrl": "..." }`.** It must be an absolute `http(s)`
  URL without user info, whose origin (scheme, host, port) exactly matches one of `Payments:AllowedReturnOrigins`.
  Everything else (other hosts, look-alike hosts, other ports or schemes, relative or scheme-relative URLs) is a
  `400` validation error, and nothing is sent to the provider. An empty allow list rejects every return URL.
  Compose sets it to the web app's public origin (`WEB_PUBLIC_ORIGIN`, default `http://localhost:$WEB_PORT`).
- The return URL travels to the provider when the provider payment is created, as with Stripe's `success_url`.
  Resuming an existing pending payment keeps the first return URL (the provider call is idempotent).
- **The sandbox redirects** with `303 See Other` to the return URL plus `?payment=succeeded|failed`. That query value is
  only a hint for the page; the result is still applied by the signed webhook, which may arrive a moment later.
- **Bookings expose the last attempt:** `lastPaymentStatus` (`Pending` / `Succeeded` / `Failed`, null if never started)
  and `lastPaymentFailureReason` (only when it failed). They come from the newest payment row of each booking, read in
  one query for lists.

## Consequences
- ✅ Same-tab checkout: the web sends the traveler to the provider and gets them back, no popup or 3-second polling loop.
- ✅ Declines are visible ("Card declined, try again") without a separate endpoint.
- ✅ No open redirect: the allow list is server-side configuration, never client input.
- ⚠️ After the redirect the webhook may not have landed yet: clients should poll briefly while
  `lastPaymentStatus` is `Pending`.
- ⚠️ Each extra web origin (preview deployments) must be added to the allow list.
