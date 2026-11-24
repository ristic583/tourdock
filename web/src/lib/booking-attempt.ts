import type { Booking, BookingType, Payment } from "@/lib/api/contracts";
import { ApiError } from "@/lib/api/problem";

export type BookingInput = { departureId: string; type: BookingType; guests: number };

/**
 * One traveler action ("book this departure"), kept across retries so a retry can't double-book:
 *  - the same Idempotency-Key is sent until the API has answered the booking request,
 *    so a lost response is replayed by the API instead of creating a second booking;
 *  - once the booking exists, it is never created again: the traveler moves on to its checkout page,
 *    where only the payment is started (and retried, for example while the provider is briefly down).
 */
export type BookingAttempt = {
  input: BookingInput;
  idempotencyKey: string;
  bookingId?: string;
};

export type BookingApi = {
  createBooking(input: BookingInput, idempotencyKey: string): Promise<Booking>;
  /** `returnUrl`: where the payment page sends the traveler after Pay or Decline (an allow-listed origin). */
  createPayment(bookingId: string, returnUrl?: string): Promise<Payment>;
};

export function newAttempt(input: BookingInput, makeKey: () => string = () => crypto.randomUUID()): BookingAttempt {
  return { input, idempotencyKey: makeKey() };
}

/** Same departure, type and guests: the retry belongs to the same action. */
export function isSameInput(a: BookingInput, b: BookingInput): boolean {
  return a.departureId === b.departureId && a.type === b.type && a.guests === b.guests;
}

/**
 * Whether a failed booking request may have created a booking anyway (no response, or a server error).
 * Then the key must be kept for the retry. A 4xx means the API rejected it, so a new action may start fresh.
 */
export function mayHaveBeenCreated(error: unknown): boolean {
  return !(error instanceof ApiError) || error.status === 0 || error.status >= 500;
}

/**
 * Holds seats for the attempt and returns the booking id. Resuming an attempt whose booking already exists
 * returns that booking without calling the API again, so a retry can never book twice.
 */
export async function holdBooking(attempt: BookingAttempt, api: Pick<BookingApi, "createBooking">): Promise<string> {
  if (!attempt.bookingId) {
    const booking = await api.createBooking(attempt.input, attempt.idempotencyKey);
    attempt.bookingId = booking.id;
  }
  return attempt.bookingId;
}

/** Waits between payment retries: 1 s, 2 s, 4 s… */
export const PAYMENT_BACKOFF_MS = [1000, 2000, 4000];

function isUnavailable(error: unknown): boolean {
  return error instanceof ApiError && error.status === 503;
}

/**
 * Asks the API for the sandbox checkout link of a held booking. The API reuses a pending payment (and the return URL it
 * was created with), so calling this again is safe. A 503 means the payment provider is briefly down: retry with
 * backoff before giving up.
 */
export async function startPayment(
  bookingId: string,
  api: Pick<BookingApi, "createPayment">,
  {
    returnUrl,
    backoff = PAYMENT_BACKOFF_MS,
    sleep = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms)),
  }: { returnUrl?: string; backoff?: number[]; sleep?: (ms: number) => Promise<void> } = {},
): Promise<string> {
  for (let attempt = 0; ; attempt++) {
    try {
      const payment = await api.createPayment(bookingId, returnUrl);
      if (!payment.checkoutUrl) throw new Error("The payment provider did not return a checkout link.");
      return payment.checkoutUrl;
    } catch (error) {
      if (!isUnavailable(error) || attempt >= backoff.length) throw error;
      await sleep(backoff[attempt]);
    }
  }
}

/** Where the payment page sends the traveler back to: this booking's checkout page on the current origin. */
export function checkoutReturnUrl(origin: string, bookingId: string): string {
  return new URL(`/bookings/${encodeURIComponent(bookingId)}/checkout`, origin).toString();
}

/** How often, and how many times, a booking is re-read while its payment waits for the provider's webhook. */
export const PAYMENT_POLL_MS = 1000;
export const PAYMENT_POLL_TRIES = 8;
/** How often an expired hold is re-read until the API has released it. */
export const EXPIRED_HOLD_POLL_MS = 5000;

/**
 * When the checkout page should re-read the booking (ms), or `false` to stop.
 *  - a payment is pending (the traveler just came back, the webhook may land a moment later): every second, a few times;
 *  - the hold ran out on the clock but the booking is still Pending: until the API marks it Expired.
 */
export function checkoutRefetchInterval(
  booking: Pick<Booking, "status" | "lastPaymentStatus" | "holdExpiresAt">,
  reads: number,
  nowMs: number,
): number | false {
  if (booking.status !== "Pending") return false;
  // Booking and payment are read separately; a webhook can settle the payment between those reads.
  // A successful payment must keep polling until the booking itself leaves Pending.
  if ((booking.lastPaymentStatus === "Pending" || booking.lastPaymentStatus === "Succeeded") && reads < PAYMENT_POLL_TRIES) return PAYMENT_POLL_MS;
  if (booking.holdExpiresAt && new Date(booking.holdExpiresAt).getTime() <= nowMs) return EXPIRED_HOLD_POLL_MS;
  return false;
}
