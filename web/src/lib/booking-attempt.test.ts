import { describe, expect, it, vi } from "vitest";

import type { Booking, Payment } from "@/lib/api/contracts";
import { networkError, toApiError } from "@/lib/api/problem";

import {
  checkoutRefetchInterval,
  checkoutReturnUrl,
  EXPIRED_HOLD_POLL_MS,
  holdBooking,
  isSameInput,
  mayHaveBeenCreated,
  newAttempt,
  PAYMENT_POLL_MS,
  PAYMENT_POLL_TRIES,
  startPayment,
  type BookingApi,
} from "./booking-attempt";

const input = { departureId: "dep-1", type: "Shared" as const, guests: 2 };
const booking = { id: "booking-1" } as Booking;
const payment = { paymentId: "pay-1", checkoutUrl: "http://localhost:8090/checkout/pay-1" } as Payment;
const unavailable = toApiError(503, { status: 503, title: "Payment provider unavailable" });
const noWait = { sleep: vi.fn(async () => {}) };

function api(overrides: Partial<BookingApi> = {}): BookingApi & { createBooking: ReturnType<typeof vi.fn>; createPayment: ReturnType<typeof vi.fn> } {
  return {
    createBooking: vi.fn(async () => booking),
    createPayment: vi.fn(async () => payment),
    ...overrides,
  } as never;
}

describe("holdBooking", () => {
  it("books once and remembers the booking", async () => {
    const deps = api();
    const attempt = newAttempt(input, () => "key-1");

    expect(await holdBooking(attempt, deps)).toBe("booking-1");
    expect(await holdBooking(attempt, deps)).toBe("booking-1");
    expect(deps.createBooking).toHaveBeenCalledTimes(1);
    expect(deps.createBooking).toHaveBeenCalledWith(input, "key-1");
  });

  it("sends the same idempotency key again when the booking response was lost", async () => {
    const deps = api({ createBooking: vi.fn().mockRejectedValueOnce(networkError()).mockResolvedValueOnce(booking) });
    const attempt = newAttempt(input, () => "key-1");

    await expect(holdBooking(attempt, deps)).rejects.toThrow();
    await holdBooking(attempt, deps);

    expect(deps.createBooking.mock.calls.map((call) => call[1])).toEqual(["key-1", "key-1"]);
  });
});

describe("startPayment", () => {
  it("returns the checkout link", async () => {
    expect(await startPayment("booking-1", api(), noWait)).toBe(payment.checkoutUrl);
  });

  it("sends the return URL with every try", async () => {
    const returnUrl = "http://localhost:3000/bookings/booking-1/checkout";
    const deps = api({ createPayment: vi.fn().mockRejectedValueOnce(unavailable).mockResolvedValueOnce(payment) });
    await startPayment("booking-1", deps, { ...noWait, returnUrl });
    expect(deps.createPayment.mock.calls).toEqual([["booking-1", returnUrl], ["booking-1", returnUrl]]);
  });

  it("retries a 503 with backoff, then succeeds", async () => {
    const sleep = vi.fn(async () => {});
    const deps = api({ createPayment: vi.fn().mockRejectedValueOnce(unavailable).mockRejectedValueOnce(unavailable).mockResolvedValueOnce(payment) });

    expect(await startPayment("booking-1", deps, { backoff: [10, 20, 40], sleep })).toBe(payment.checkoutUrl);
    expect(sleep.mock.calls).toEqual([[10], [20]]);
  });

  it("gives up after the last backoff step", async () => {
    const deps = api({ createPayment: vi.fn().mockRejectedValue(unavailable) });
    await expect(startPayment("booking-1", deps, { backoff: [1, 1], sleep: noWait.sleep })).rejects.toBe(unavailable);
    expect(deps.createPayment).toHaveBeenCalledTimes(3);
  });

  it("doesn't retry a rejection that won't change", async () => {
    const expired = toApiError(409, { status: 409, code: "Booking.HoldExpired" });
    const deps = api({ createPayment: vi.fn().mockRejectedValue(expired) });
    await expect(startPayment("booking-1", deps, noWait)).rejects.toBe(expired);
    expect(deps.createPayment).toHaveBeenCalledTimes(1);
  });

  it("fails clearly when the provider returns no checkout link", async () => {
    const deps = api({ createPayment: vi.fn(async () => ({ ...payment, checkoutUrl: null })) });
    await expect(startPayment("booking-1", deps, noWait)).rejects.toThrow(/checkout link/);
  });
});

describe("attempt rules", () => {
  it("keeps the key only when the booking may exist", () => {
    expect(mayHaveBeenCreated(networkError())).toBe(true);
    expect(mayHaveBeenCreated(unavailable)).toBe(true);
    expect(mayHaveBeenCreated(new TypeError("fetch failed"))).toBe(true);
    expect(mayHaveBeenCreated(toApiError(409, { status: 409, code: "Slot.NotEnoughSeats" }))).toBe(false);
  });

  it("treats a changed type or guest count as a new action", () => {
    expect(isSameInput(input, { ...input })).toBe(true);
    expect(isSameInput(input, { ...input, type: "Private" })).toBe(false);
    expect(isSameInput(input, { ...input, guests: 3 })).toBe(false);
  });
});

describe("checkout return", () => {
  it("comes back to the booking's checkout page on the same origin", () => {
    expect(checkoutReturnUrl("http://localhost:3000", "b-1")).toBe("http://localhost:3000/bookings/b-1/checkout");
  });

  const now = Date.parse("2027-06-09T10:00:00Z");
  const pending = { status: "Pending" as const, lastPaymentStatus: "Pending" as const, holdExpiresAt: "2027-06-09T10:10:00Z" };

  it("re-reads every second while the payment waits for the webhook, a few times", () => {
    expect(checkoutRefetchInterval(pending, 0, now)).toBe(PAYMENT_POLL_MS);
    expect(checkoutRefetchInterval(pending, PAYMENT_POLL_TRIES - 1, now)).toBe(PAYMENT_POLL_MS);
    expect(checkoutRefetchInterval(pending, PAYMENT_POLL_TRIES, now)).toBe(false);
  });

  it("keeps polling when payment success arrives before the booking confirmation", () => {
    const paidButPending = { ...pending, lastPaymentStatus: "Succeeded" as const };
    expect(checkoutRefetchInterval(paidButPending, 0, now)).toBe(PAYMENT_POLL_MS);
    expect(checkoutRefetchInterval(paidButPending, PAYMENT_POLL_TRIES - 1, now)).toBe(PAYMENT_POLL_MS);
    expect(checkoutRefetchInterval(paidButPending, PAYMENT_POLL_TRIES, now)).toBe(false);
  });

  it("stops once the payment is settled or the booking moved on", () => {
    expect(checkoutRefetchInterval({ ...pending, lastPaymentStatus: "Failed" }, 0, now)).toBe(false);
    expect(checkoutRefetchInterval({ ...pending, lastPaymentStatus: null }, 0, now)).toBe(false);
    expect(checkoutRefetchInterval({ ...pending, status: "Confirmed", lastPaymentStatus: "Succeeded" }, 0, now)).toBe(false);
  });

  it("keeps asking after the hold ran out, until the API releases it", () => {
    const expired = { ...pending, lastPaymentStatus: null, holdExpiresAt: "2027-06-09T09:59:59Z" };
    expect(checkoutRefetchInterval(expired, 20, now)).toBe(EXPIRED_HOLD_POLL_MS);
    expect(checkoutRefetchInterval({ ...expired, status: "Expired" }, 20, now)).toBe(false);
  });
});
