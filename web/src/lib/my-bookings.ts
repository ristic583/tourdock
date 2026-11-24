import type { Booking, Money, RefundTier } from "@/lib/api/contracts";
import { freeCancellationHours, policySegments, type PolicySegment } from "@/lib/availability";
import { formatInZone, formatMoney } from "@/lib/format";

export type BookingTab = "upcoming" | "past" | "cancelled";

export const TABS: { id: BookingTab; label: string }[] = [
  { id: "upcoming", label: "Upcoming" },
  { id: "past", label: "Past" },
  { id: "cancelled", label: "Cancelled & expired" },
];

const HOUR = 60 * 60 * 1000;

/**
 * Short, human-friendly booking reference derived from the id, e.g. "TD-7Q4K-2F9A". Must match
 * Booking.ReferenceFor in the API, which the emails use.
 */
export function bookingReference(id: string): string {
  const hex = id.replace(/-/g, "").slice(-8).toUpperCase();
  return `TD-${hex.slice(0, 4)}-${hex.slice(4)}`;
}

export function parseTab(value: string | string[] | undefined): BookingTab {
  return value === "past" || value === "cancelled" ? value : "upcoming";
}

export function tabOf(booking: Booking, now: number): BookingTab {
  if (booking.status === "Cancelled" || booking.status === "Expired") return "cancelled";
  if (booking.status === "Completed") return "past";
  return new Date(booking.startsAt).getTime() > now ? "upcoming" : "past";
}

/** Upcoming soonest first; past and cancelled most recent first. */
export function groupBookings(bookings: Booking[], now: number): Record<BookingTab, Booking[]> {
  const groups: Record<BookingTab, Booking[]> = { upcoming: [], past: [], cancelled: [] };
  for (const booking of bookings) groups[tabOf(booking, now)].push(booking);
  const time = (booking: Booking) => new Date(booking.startsAt).getTime();
  groups.upcoming.sort((a, b) => time(a) - time(b));
  groups.past.sort((a, b) => time(b) - time(a));
  groups.cancelled.sort((a, b) => time(b) - time(a));
  return groups;
}

export function seatsText(booking: Booking): string {
  return booking.type === "Private" ? "Private trip" : `Shared · ${booking.guests} ${booking.guests === 1 ? "seat" : "seats"}`;
}

export type Note = { text: string; tone: "primary" | "urgent" | "muted" };

function at(booking: Booking, hoursBefore: number): string {
  const when = formatInZone(new Date(new Date(booking.startsAt).getTime() - hoursBefore * HOUR), booking.timeZoneId);
  return `${when.date}, ${when.time}`;
}

/** The refund window the booking is in right now (by hours left until departure). */
export function currentSegment(tiers: RefundTier[], startsAt: string, now: number): PolicySegment {
  const hoursLeft = (new Date(startsAt).getTime() - now) / HOUR;
  const segments = policySegments(tiers);
  return segments.find((segment) => hoursLeft >= segment.fromHours) ?? segments.at(-1)!;
}

/** One line under a booking: what cancelling means right now, or what happened to it. */
export function bookingNote(booking: Booking, now: number): Note | undefined {
  switch (booking.status) {
    case "Confirmed": {
      if (new Date(booking.startsAt).getTime() <= now) return undefined;
      const segment = currentSegment(booking.cancellationPolicy, booking.startsAt, now);
      const free = freeCancellationHours(booking.cancellationPolicy);
      if (segment.refundPercent >= 100 && free !== undefined) return { text: `Free cancellation until ${at(booking, free)}`, tone: "primary" };
      if (segment.refundPercent > 0) return { text: `${segment.refundPercent}% refund window, ends ${at(booking, segment.fromHours)}`, tone: "urgent" };
      return { text: "No refund if cancelled now", tone: "muted" };
    }
    case "Cancelled": {
      const by = booking.cancelledBy === "Customer" ? "You cancelled" : booking.cancelledBy === "Agency" ? "Cancelled by the agency" : "Cancelled";
      const refund = booking.refundedAmount.amount > 0 ? `refunded ${formatMoney(booking.refundedAmount)}` : "no refund";
      return { text: `${by} · ${refund}${booking.cancellationReason ? ` · “${booking.cancellationReason}”` : ""}`, tone: "muted" };
    }
    case "Expired":
      // A payment that completed after the hold expired was taken and is given back.
      return booking.refundedAmount.amount > 0
        ? { text: `Hold expired before the payment came through · ${formatMoney(booking.refundedAmount)} refunded`, tone: "muted" }
        : { text: "Hold expired before payment · not charged", tone: "muted" };
    default:
      return undefined;
  }
}

/** "paid", "not paid yet"… under the total. */
export function paymentText(booking: Booking): string {
  if (booking.status === "Pending") return "not paid yet";
  if (booking.refundedAmount.amount > 0) return `${formatMoney(booking.refundedAmount)} refunded`;
  if (booking.status === "Expired") return "not charged";
  return booking.paidAmount.amount > 0 ? "paid" : "";
}

export type RefundPreview = { removed: number; removedValue: Money; refund: Money; newTotal: Money; percent: number };

/**
 * What cancelling seats would refund now. Built on the API's `refundIfCancelledNow` (the refund for the whole booking
 * at this moment), scaled to the seats removed, so the preview follows the server's policy maths.
 */
export function refundPreview(booking: Booking, keep: number): RefundPreview {
  const removed = booking.guests - keep;
  const currency = booking.totalPrice.currency;
  const cents = (amount: number) => Math.round(amount * 100) / 100;
  const whole = keep === 0 || booking.type === "Private";
  const share = whole ? 1 : removed / booking.guests;
  // The tier is measured against the seats still on the booking. Not against paidAmount: that stays the original
  // gross after a partial cancellation, while the API's refund only covers the remaining seats.
  const paid = booking.totalPrice.amount;
  return {
    removed,
    removedValue: { amount: cents(whole ? booking.totalPrice.amount : booking.unitPrice.amount * removed), currency },
    refund: { amount: cents(booking.refundIfCancelledNow.amount * share), currency },
    newTotal: { amount: cents(whole ? 0 : booking.unitPrice.amount * keep), currency },
    percent: paid ? Math.round((booking.refundIfCancelledNow.amount / paid) * 100) : 0,
  };
}
