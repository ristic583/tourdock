import { describe, expect, it } from "vitest";

import type { AgencyStats, Slot } from "@/lib/api/contracts";

import { agencyKpis, countRuleDepartures, describeRule, formatDays, overviewRanges, parseDays, slotKind, startOfWeek, weekdayOf } from "./agency-stats";

const eur = (amount: number) => ({ amount, currency: "EUR" });

function slot(overrides: Partial<Slot> = {}): Slot {
  return {
    id: "s", tourId: "t", vehicleId: "v1", scheduleRuleId: null, startsAt: "2027-06-09T07:00:00Z", endsAt: "2027-06-09T10:00:00Z",
    localDate: "2027-06-09", capacity: 12, reservedSeats: 3, remainingSeats: 9, bookingMode: "Both", isPrivatelyBooked: false, status: "Open",
    ...overrides,
  };
}

function stats(overrides: Partial<AgencyStats> = {}): AgencyStats {
  return {
    from: "2027-06-01", to: "2027-06-30", departures: 0, vehiclesInUse: 0, seatsOnSale: 0, seatsSold: 0, occupancyPercent: 0,
    revenue: { paid: eur(0), refunded: eur(0), net: eur(0) }, today: { departures: 1, guests: 3 }, pendingHolds: 1,
    ...overrides,
  };
}

describe("overviewRanges", () => {
  it("covers the calendar month, the next 7 days and the next 30 days", () => {
    expect(overviewRanges("2027-06-09")).toEqual({
      month: { from: "2027-06-01", to: "2027-06-30" },
      week: { from: "2027-06-09", to: "2027-06-15" },
      next30: { from: "2027-06-09", to: "2027-07-08" },
    });
  });

  it("ends February on its last day, leap years included", () => {
    expect(overviewRanges("2028-02-29").month).toEqual({ from: "2028-02-01", to: "2028-02-29" });
    expect(overviewRanges("2027-12-31").month).toEqual({ from: "2027-12-01", to: "2027-12-31" });
  });
});

describe("agencyKpis", () => {
  it("takes revenue from the month, departures from the week and occupancy from the next 30 days", () => {
    const kpis = agencyKpis({
      month: stats({ revenue: { paid: eur(300), refunded: eur(50), net: eur(250) } }),
      week: stats({ departures: 2, vehiclesInUse: 1, seatsOnSale: 16, seatsSold: 6, occupancyPercent: 38 }),
      next30: stats({ departures: 9, vehiclesInUse: 3, seatsOnSale: 90, seatsSold: 30, occupancyPercent: 33 }),
    });
    expect(kpis).toEqual({
      departuresNext7Days: 2,
      vehiclesInUse: 1,
      seatsSold: 30,
      seatsOnSale: 90,
      occupancyPercent: 33,
      revenueThisMonth: eur(250),
      refundedThisMonth: eur(50),
      guestsToday: 3,
      departuresToday: 1,
      pendingHolds: 1,
    });
  });

  it("leaves out refunds when nothing was refunded", () => {
    expect(agencyKpis({ month: stats(), week: stats(), next30: stats() })).toMatchObject({ revenueThisMonth: eur(0), refundedThisMonth: null });
  });
});

describe("schedule rules", () => {
  it("reads and writes the API's day flags", () => {
    expect(parseDays("Saturday, Monday")).toEqual(["Monday", "Saturday"]);
    expect(formatDays(["Saturday", "Monday"])).toBe("Monday, Saturday");
  });

  it("finds weekdays and week starts", () => {
    expect(weekdayOf("2027-06-09")).toBe("Wednesday");
    expect(startOfWeek("2027-06-09")).toBe("2027-06-07");
    expect(startOfWeek("2027-06-07")).toBe("2027-06-07");
  });

  it("counts the departures a rule creates", () => {
    expect(countRuleDepartures(["Monday", "Wednesday"], "2027-06-07", "2027-06-20")).toBe(4);
    expect(countRuleDepartures([], "2027-06-07", "2027-06-20")).toBe(0);
  });

  it("describes a rule", () => {
    expect(describeRule({ days: "Monday, Friday", startTime: "09:00:00", validFrom: "2027-05-01", validUntil: null })).toBe("Every Mon, Fri at 09:00, from Sat 1 May 2027");
  });
});

describe("slotKind", () => {
  it("picks the calendar chip", () => {
    expect(slotKind(slot())).toBe("shared");
    expect(slotKind(slot({ remainingSeats: 0 }))).toBe("full");
    expect(slotKind(slot({ isPrivatelyBooked: true }))).toBe("private");
    expect(slotKind(slot({ status: "Blocked" }))).toBe("blocked");
  });
});
