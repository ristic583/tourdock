# ADR 0011: Payments, webhooks and the transactional outbox

- **Status:** Accepted
- **Date:** 2026-11-06

## Context
TourDock must never charge twice, never lose a payment or refund, and never confirm a booking that wasn't paid,
even when the provider is slow, down, or delivers events twice or out of order.
Real payments are out of scope, so we use a **sandbox provider**. It's built so a real provider (Stripe, Adyen) could be swapped in.

## Decision

**Provider abstraction.** The application talks to `IPaymentProvider` (create payment, create refund).
`HttpPaymentProvider` is a typed `HttpClient` with the standard resilience handler (timeouts, retries with jitter,
circuit breaker). **Every call sends our own id as the `Idempotency-Key`** (payment id / refund id), so retrying a POST is safe.

**Payment sandbox** (`src/TourDock.PaymentSandbox`, its own container). It behaves like a hosted provider:
- an API key, idempotency keys, and a hosted checkout page with Pay / Decline buttons (`http://localhost:8090/checkout/{id}`)
- refunds that settle asynchronously
- **signed webhooks delivered at least once**, retried with backoff

It's independent of the TourDock code on purpose.

**Paying a booking:** `POST /bookings/{id}/payments`
1. The payment row is saved **before** calling the provider. A timeout then leaves a pending payment that the next attempt
   reuses (same idempotency key), so there's no duplicate charge.
2. At most one pending payment per booking (partial unique index).
3. If the provider is down, the API returns `503` with `Retry-After`. The booking stays held.

**Webhooks:** `POST /webhooks/payments`
- **Authenticity:** an HMAC-SHA256 signature over `"{timestamp}.{raw body}"` with a shared secret, compared in constant time.
  Timestamps older than 5 minutes are rejected (replay protection).
- **Exactly-once effect:** the event id is stored in `processed_events` **in the same transaction** as the state change.
  A re-delivered event is acknowledged and does nothing.
- **Out-of-order safety:** every transition checks the current state.
- **Late payment:** if the money arrives after the booking expired or was cancelled, the booking is not resurrected.
  The payment is **refunded automatically**. If the booking is still pending (its seats are still held), a payment
  that completes slightly after the 15-minute hold is accepted.

**Refunds via the transactional outbox**
- A cancellation saves the booking change, a `Refund` row and an `outbox_messages` row in **one transaction**.
  Either all of them happen or none.
- `OutboxProcessor` (background) claims due messages with `FOR UPDATE SKIP LOCKED`, so several instances can run it,
  and calls the provider.
- Failures retry with exponential backoff (2ˢ, capped at 1 hour). After 10 attempts the message is **parked** with its last error.
- Handlers are idempotent: they check the refund status, and the provider dedupes by refund id. **At-least-once** delivery is therefore safe.

**Secrets:** API key and webhook secret come from environment variables (`PAYMENTS_API_KEY`, `PAYMENTS_WEBHOOK_SECRET`),
are validated at startup, and are never in source. Compose has local-only defaults.

## Consequences
- ✅ No double charges, no lost refunds, and no unpaid confirmations under retries, outages, duplicates or reordering.
  This is covered by integration tests and verified end to end against the sandbox.
- ✅ The outbox is general-purpose. Notification emails use it too ([ADR 0018](0018-notifications-and-background-jobs.md)).
- ⚠️ Parked outbox messages need someone to look at them (the readiness check reports them as `Degraded`, see [ADR 0020](0020-observability-and-hardening.md)).
- ⚠️ Partial captures, disputes/chargebacks and payouts to agencies are out of scope.

## Amendment (2026-11-16): declined refunds
- A `refund.failed` webhook used to end the refund, while the booking already counted the money as refunded. Now a
  declined refund is sent again after 15 minutes and 2 hours, each time under a new idempotency key (the provider
  would replay the decline for the old one). After three declines it waits for an admin: `GET /api/v1/admin/refunds`
  lists it, `POST /admin/refunds/{id}/retry` sends it again (audited), `tourdock.refunds.failed` counts it and the
  readiness check reports `Degraded` while any is waiting.
- Refunds of one payment can never add up to more than was paid (domain guard plus a database check on the amount).
