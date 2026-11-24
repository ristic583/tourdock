import type { Booking, CancellationResult, Payment } from "@/lib/api/contracts";
import type { BookingApi } from "@/lib/booking-attempt";

import { browserApi } from "./browser";
import { unwrap } from "./client";

/** Booking calls from Client Components (through the BFF). */
export const bookingsApi: BookingApi & {
  get(bookingId: string): Promise<Booking>;
  cancel(bookingId: string, reason?: string): Promise<CancellationResult>;
  reduceGuests(bookingId: string, guests: number): Promise<CancellationResult>;
} = {
  createBooking: (input, idempotencyKey) =>
    unwrap<Booking>(browserApi.POST("/api/v1/bookings", { params: { header: { "Idempotency-Key": idempotencyKey } }, body: input })),
  createPayment: (bookingId, returnUrl) =>
    unwrap<Payment>(
      browserApi.POST("/api/v1/bookings/{bookingId}/payments", { params: { path: { bookingId } }, body: returnUrl ? { returnUrl } : undefined }),
    ),
  get: (bookingId) => unwrap<Booking>(browserApi.GET("/api/v1/bookings/{bookingId}", { params: { path: { bookingId } } })),
  cancel: (bookingId, reason) =>
    unwrap<CancellationResult>(
      browserApi.POST("/api/v1/bookings/{bookingId}/cancel", { params: { path: { bookingId } }, body: { reason: reason?.trim() || null } }),
    ),
  reduceGuests: (bookingId, guests) =>
    unwrap<CancellationResult>(browserApi.POST("/api/v1/bookings/{bookingId}/reduce-guests", { params: { path: { bookingId } }, body: { guests } })),
};
