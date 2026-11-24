# ADR 0010: Booking flow, idempotency and concurrency

- **Status:** Accepted
- **Date:** 2026-11-06

## Flow
`POST /api/v1/bookings` (with an `Idempotency-Key` header) creates a **Pending** booking that holds the seats for **15 minutes**.
Payment ([ADR 0011](0011-payments-webhooks-outbox.md)) confirms it. Otherwise a background job **expires** it and releases the seats.
After the departure ends, confirmed bookings become **Completed**, which unlocks reviews.
Customers can **cancel** (refund by policy) or **reduce guests** (partial refund). Agencies can cancel a booking with a full refund.

## Concurrency: the last-seat race
The first implementation used only **optimistic concurrency**: a row version (`xmin`) on the departure, plus 3 retries.
A test with **6 travelers racing for 4 seats** showed the problem under contention. Each round only one request wins,
so requests ran out of retries **while seats were still free** (3 bookings instead of 4).

**Decision:** the booking path takes a **pessimistic row lock** on the departure:
`SELECT ... FOR UPDATE` inside a short transaction.
- Concurrent bookings of the same departure **queue** for a few milliseconds. Each sees the seats left by the one before,
  then either books or gets a clear `409 Slot.NotEnoughSeats`.
- The lock covers only the reservation. It's **never held during payment**: the Pending booking *is* the hold.
- `lock_timeout = 5s` means extreme contention returns `409` ("try again") instead of hanging.
- Backstops stay in place: the row version, `CHECK (reserved_seats BETWEEN 0 AND capacity)`, and retries on the
  low-contention paths (cancel, reduce, expire).

Result: 6 racing for 4 seats gives exactly 4 bookings and 2 conflicts, and the departure ends at 0 seats left. The test is stable across repeated runs.

## Idempotency (double clicks, client retries)
- `Idempotency-Key` is **required** on create (1–64 characters; a UUID works).
- The key is stored **on the booking, in the same transaction**, with a unique index on `(customer_id, idempotency_key)`.
  - The same key with the same request returns the original booking (`200` + `Idempotent-Replayed: true`). No second reservation.
  - The same key with a different request returns `409 Booking.IdempotencyKeyReused`.
  - Two identical requests arriving at once: the second waits on the departure lock and then sees the first one's booking.
    If they still collide, the unique index decides.
- Storing the key with the business row (instead of a separate cache) means a crash can never leave a key that's
  recorded but has no booking, or the other way round.

## Other rules
- Only the owner can see or change a booking. Anyone else gets `404`.
- Agencies see bookings on their own departures, including the customer's name and email (needed to run the trip). See the PII notes in a later ADR.
- Search results may show seat counts that are up to 30 seconds old (cache). Booking always re-checks under the lock.

## Consequences
- ✅ No overbooking, no lost bookings under contention, and safe retries.
- ⚠️ Very hot departures serialize their bookings. That's fine at this scale. At very large scale you'd use an atomic
  `UPDATE ... WHERE remaining >= n` or split the inventory.
