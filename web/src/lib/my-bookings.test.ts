import { describe, expect, it } from "vitest";

import type { Booking } from "@/lib/api/contracts";

import { bookingNote, bookingReference, groupBookings, parseTab, paymentText, refundPreview, tabOf } from "./my-bookings";

const NOW = Date.parse("2027-06-10T12:00:00Z");
const eur = (amount: number) => ({ amount, currency: "EUR" });

function booking(overrides: Partial<Booking> = {}): Booking {
  return {
    id: "b1",
    tourId: "t1",
    tourTitle: "Harbour Cruise",
    departureId: "d1",
    type: "Shared",
    guests: 3,
    status: "Confirmed",
    unitPrice: eur(49),
    totalPrice: eur(147),
    paidAmount: eur(147),
    refundedAmount: eur(0),
    startsAt: "2027-06-14T16:00:00Z",
    localStart: "2027-06-14T18:00:00",
    timeZoneId: "Europe/Zagreb",
    holdExpiresAt: null,
    cancellationPolicy: [
      { hoursBeforeStart: 72, refundPercent: 100 },
      { hoursBeforeStart: 24, refundPercent: 50 },
    ],
    refundIfCancelledNow: eur(147),
    createdAt: "2027-06-01T10:00:00Z",
    cancelledAt: null,
    cancelledBy: null,
    cancellationReason: null,
    lastPaymentStatus: "Succeeded",
    lastPaymentFailureReason: null,
    ...overrides,
  };
}

describe("tabs", () => {
  it("sorts bookings into upcoming, past and cancelled", () => {
    expect(tabOf(booking(), NOW)).toBe("upcoming");
    expect(tabOf(booking({ status: "Pending" }), NOW)).toBe("upcoming");
    expect(tabOf(booking({ startsAt: "2027-06-01T08:00:00Z" }), NOW)).toBe("past");
    expect(tabOf(booking({ status: "Completed" }), NOW)).toBe("past");
    expect(tabOf(booking({ status: "Expired" }), NOW)).toBe("cancelled");
    expect(tabOf(booking({ status: "Cancelled" }), NOW)).toBe("cancelled");
  });

  it("lists upcoming soonest first", () => {
    const later = booking({ id: "later", startsAt: "2027-07-01T08:00:00Z" });
    const sooner = booking({ id: "sooner", startsAt: "2027-06-11T08:00:00Z" });
    expect(groupBookings([later, sooner], NOW).upcoming.map((b) => b.id)).toEqual(["sooner", "later"]);
  });

  it("reads the tab from the URL", () => {
    expect(parseTab("past")).toBe("past");
    expect(parseTab(undefined)).toBe("upcoming");
    expect(parseTab("nope")).toBe("upcoming");
  });
});

describe("bookingNote", () => {
  it("shows the free cancellation deadline in the tour's time zone", () => {
    expect(bookingNote(booking(), NOW)).toEqual({ text: "Free cancellation until Fri 11 Jun, 18:00", tone: "primary" });
  });

  it("warns in orange inside a partial refund window", () => {
    expect(bookingNote(booking({ startsAt: "2027-06-12T16:00:00Z" }), NOW)).toEqual({ text: "50% refund window, ends Fri 11 Jun, 18:00", tone: "urgent" });
  });

  it("explains cancellations and expiries", () => {
    expect(bookingNote(booking({ status: "Cancelled", cancelledBy: "Customer", refundedAmount: eur(147) }), NOW)?.text).toBe("You cancelled · refunded €147");
    expect(bookingNote(booking({ status: "Expired" }), NOW)?.text).toMatch(/not charged/);
  });

  it("shows a payment that came through after the hold expired as refunded, not as not charged", () => {
    const late = booking({ status: "Expired", paidAmount: eur(98), refundedAmount: eur(98) });
    expect(bookingNote(late, NOW)?.text).toBe("Hold expired before the payment came through · €98 refunded");
    expect(paymentText(late)).toBe("€98 refunded");
  });
});

describe("paymentText", () => {
  it("says what happened to the money", () => {
    expect(paymentText(booking())).toBe("paid");
    expect(paymentText(booking({ status: "Pending" }))).toBe("not paid yet");
    expect(paymentText(booking({ status: "Cancelled", refundedAmount: eur(73.5) }))).toBe("€73.50 refunded");
  });
});

describe("refundPreview", () => {
  it("scales the API's refund to the seats removed", () => {
    const preview = refundPreview(booking({ refundIfCancelledNow: eur(73.5) }), 2);
    expect(preview).toMatchObject({ removed: 1, removedValue: eur(49), refund: eur(24.5), newTotal: eur(98), percent: 50 });
  });

  it("refunds the whole amount when every seat is cancelled", () => {
    expect(refundPreview(booking(), 0)).toMatchObject({ removed: 3, refund: eur(147), newTotal: eur(0), percent: 100 });
  });

  it("keeps the 50% tier after an earlier partial cancellation in the same tier", () => {
    // 2 seats paid (€80), 1 cancelled at 50% (€20 back); cancelling the last €40 seat refunds €20.
    const reduced = booking({
      guests: 1,
      unitPrice: eur(40),
      totalPrice: eur(40),
      paidAmount: eur(80),
      refundedAmount: eur(20),
      refundIfCancelledNow: eur(20),
    });
    expect(refundPreview(reduced, 0)).toMatchObject({ refund: eur(20), percent: 50 });
  });

  it("measures the policy tier against the seats still booked after an earlier partial refund", () => {
    // 2 seats paid (€90), 1 already cancelled and refunded (€45); cancelling the last seat in the 100% tier.
    const reduced = booking({
      guests: 1,
      unitPrice: eur(45),
      totalPrice: eur(45),
      paidAmount: eur(90),
      refundedAmount: eur(45),
      refundIfCancelledNow: eur(45),
    });
    expect(refundPreview(reduced, 0)).toMatchObject({ refund: eur(45), percent: 100 });
  });

  it("treats a private trip as one unit", () => {
    const privately = booking({ type: "Private", unitPrice: eur(390), totalPrice: eur(390), paidAmount: eur(390), refundIfCancelledNow: eur(195) });
    expect(refundPreview(privately, 0)).toMatchObject({ refund: eur(195), percent: 50, newTotal: eur(0) });
  });
});

describe("bookingReference", () => {
  it("is short and stable", () => {
    expect(bookingReference("01a1b09d-b917-7a89-9fcf-cb8bda1023b4")).toBe("TD-DA10-23B4");
  });
});
