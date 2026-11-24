import { describe, expect, it } from "vitest";

import type { RatingSummary } from "@/lib/api/contracts";

import { canStillReview, distributionRows, postedLabel, reasonLabel, travelledLabel } from "./reviews";

const summary: RatingSummary = { average: 4.3, count: 10, distribution: { one: 1, two: 0, three: 1, four: 2, five: 6 } };

describe("distributionRows", () => {
  it("lists 5 to 1 stars with rounded shares", () => {
    expect(distributionRows(summary)).toEqual([
      { stars: 5, count: 6, percent: 60 },
      { stars: 4, count: 2, percent: 20 },
      { stars: 3, count: 1, percent: 10 },
      { stars: 2, count: 0, percent: 0 },
      { stars: 1, count: 1, percent: 10 },
    ]);
  });

  it("doesn't divide by zero without reviews", () => {
    const empty: RatingSummary = { average: null, count: 0, distribution: { one: 0, two: 0, three: 0, four: 0, five: 0 } };
    expect(distributionRows(empty).every((row) => row.percent === 0)).toBe(true);
  });
});

describe("canStillReview", () => {
  const now = new Date("2026-11-07T12:00:00Z").getTime();
  it("is open until the deadline the API sends", () => {
    expect(canStillReview("2026-12-01T00:00:00Z", now)).toBe(true);
    expect(canStillReview("2026-11-07T11:59:59Z", now)).toBe(false);
    expect(canStillReview(null, now)).toBe(false);
  });
});

describe("labels", () => {
  it("formats the trip month in the tour's time zone", () => {
    // 23:30 UTC on 31 May is already June in Zagreb.
    expect(travelledLabel("2026-05-31T23:30:00Z", "Europe/Zagreb")).toBe("June 2026");
    expect(travelledLabel("2026-05-31T23:30:00Z")).toBe("May 2026");
  });

  it("formats the posting date and report reasons", () => {
    expect(postedLabel("2026-11-07T20:00:00Z")).toBe("7 Nov 2026");
    expect(reasonLabel("PersonalInformation")).toBe("Shares personal information");
  });
});
