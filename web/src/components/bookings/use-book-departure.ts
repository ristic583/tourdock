"use client";

import { useRouter } from "next/navigation";
import { useRef, useState } from "react";

import { bookingsApi } from "@/lib/api/bookings";
import { ApiError } from "@/lib/api/problem";
import { holdBooking, isSameInput, mayHaveBeenCreated, newAttempt, type BookingAttempt, type BookingInput } from "@/lib/booking-attempt";

/**
 * Holds seats (idempotent across retries, see lib/booking-attempt.ts) and moves the traveler to the booking's
 * checkout page, which starts the payment. A retry after a lost response reuses the Idempotency-Key; once the
 * booking exists it is never created again.
 */
export function useBookDeparture() {
  const router = useRouter();
  const [error, setError] = useState<string>();
  const [submitting, setSubmitting] = useState(false);
  // Survives retries of the same action, so a retry reuses the Idempotency-Key or the booking already made.
  const attempt = useRef<BookingAttempt | null>(null);

  async function book(input: BookingInput) {
    const current = attempt.current;
    if (!current || (!current.bookingId && !isSameInput(current.input, input))) attempt.current = newAttempt(input);
    const active = attempt.current!;

    setSubmitting(true);
    setError(undefined);
    try {
      const bookingId = await holdBooking(active, bookingsApi);
      router.push(`/bookings/${bookingId}/checkout`);
    } catch (caught) {
      // A rejected booking (4xx) starts fresh next time; anything that may have created one keeps its key.
      if (!mayHaveBeenCreated(caught)) attempt.current = null;
      setError(caught instanceof ApiError ? caught.userMessage : "We couldn't hold your seats. Please try again.");
      setSubmitting(false);
    }
  }

  return { book, error, submitting };
}
