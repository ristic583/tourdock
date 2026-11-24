import { describe, expect, it } from "vitest";

import type { Departure } from "@/lib/api/contracts";

import { firstBookable, freeCancellationHours, policySegments, refundPercentAt, seats, segmentLabel } from "./availability";

describe("seats", () => {
  it("describes what's left and how full the departure is", () => {
    expect(seats(9, 12)).toEqual({ left: 9, capacity: 12, label: "9 of 12 left", filledPercent: 25, urgent: false });
  });

  it("is urgent only when a few seats remain", () => {
    expect(seats(3, 12).urgent).toBe(true);
    expect(seats(4, 12).urgent).toBe(false);
    expect(seats(0, 12).urgent).toBe(false);
  });
});

describe("firstBookable", () => {
  const departure = (id: string, canBookSeats: boolean, canBookPrivately: boolean) => ({ id, canBookSeats, canBookPrivately }) as Departure;

  it("skips departures the party can't book either way", () => {
    expect(firstBookable([departure("a", false, false), departure("b", false, true)])?.id).toBe("b");
    expect(firstBookable([departure("a", false, false)])).toBeUndefined();
  });
});

describe("policySegments", () => {
  it("draws the usual 72h / 24h policy as 3 : 2 : 1 with an added no-refund window", () => {
    const segments = policySegments([
      { hoursBeforeStart: 24, refundPercent: 50 },
      { hoursBeforeStart: 72, refundPercent: 100 },
    ]);
    expect(segments.map((s) => [s.refundPercent, s.fromHours, s.toHours])).toEqual([
      [100, 72, null],
      [50, 24, 72],
      [0, 0, 24],
    ]);
    const [a, b, c] = segments.map((s) => s.weight);
    expect(a / c).toBeCloseTo(3);
    expect(b / c).toBeCloseTo(2);
    expect(segments.map(segmentLabel)).toEqual(["3 days or more before", "24h–3 days before", "under 24h"]);
  });

  it("keeps a policy that refunds up to departure as it is", () => {
    expect(policySegments([{ hoursBeforeStart: 0, refundPercent: 100 }])).toHaveLength(1);
  });

  it("treats an empty policy as non-refundable", () => {
    expect(policySegments([])).toEqual([{ refundPercent: 0, fromHours: 0, toHours: null, weight: 1 }]);
  });

  it("keeps very short windows readable", () => {
    const segments = policySegments([{ hoursBeforeStart: 720, refundPercent: 100 }, { hoursBeforeStart: 2, refundPercent: 10 }]);
    expect(Math.min(...segments.map((s) => s.weight))).toBeGreaterThanOrEqual(0.12);
  });
});

describe("freeCancellationHours", () => {
  it("finds the latest full-refund deadline", () => {
    expect(freeCancellationHours([{ hoursBeforeStart: 72, refundPercent: 100 }, { hoursBeforeStart: 24, refundPercent: 50 }])).toBe(72);
    expect(freeCancellationHours([{ hoursBeforeStart: 24, refundPercent: 50 }])).toBeUndefined();
  });
});

describe("refundPercentAt", () => {
  const policy = [{ hoursBeforeStart: 72, refundPercent: 100 }, { hoursBeforeStart: 24, refundPercent: 50 }];

  it("uses the tier whose notice is still met", () => {
    expect(refundPercentAt(policy, 100)).toBe(100);
    expect(refundPercentAt(policy, 72)).toBe(100);
    expect(refundPercentAt(policy, 30)).toBe(50);
  });

  it("is non-refundable inside the last tier, after the start, or without a policy", () => {
    expect(refundPercentAt(policy, 23.5)).toBe(0);
    expect(refundPercentAt(policy, -1)).toBe(0);
    expect(refundPercentAt([], 500)).toBe(0);
  });
});
