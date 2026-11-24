import { describe, expect, it } from "vitest";

import { detailsBody, detailsDefaults, detailsSchema, policyProblems, priceProblem, pricingBody, pricingSchema, seasonProblem } from "./tour-forms";

describe("pricing", () => {
  it("requires the prices the booking mode needs", () => {
    expect(pricingSchema.safeParse({ bookingMode: "Both", pricePerPerson: "49", privatePrice: "" }).success).toBe(false);
    expect(pricingSchema.safeParse({ bookingMode: "Shared", pricePerPerson: "49", privatePrice: "" }).success).toBe(true);
    expect(pricingSchema.safeParse({ bookingMode: "Shared", pricePerPerson: "-1", privatePrice: "" }).success).toBe(false);
  });

  it("doesn't let the whole trip cost less than one seat", () => {
    const low = pricingSchema.safeParse({ bookingMode: "Both", pricePerPerson: "49", privatePrice: "40" });
    expect(low.success).toBe(false);
    expect(low.error?.issues[0]).toMatchObject({ path: ["privatePrice"], message: "The private trip price can't be lower than the price per person." });
    expect(pricingSchema.safeParse({ bookingMode: "Both", pricePerPerson: "49", privatePrice: "49" }).success).toBe(true);
    expect(pricingSchema.safeParse({ bookingMode: "Private", pricePerPerson: "500", privatePrice: "40" }).success).toBe(true);
  });

  it("sends prices only for what the mode offers", () => {
    expect(pricingBody({ bookingMode: "Shared", pricePerPerson: "49.5", privatePrice: "390" })).toEqual({ bookingMode: "Shared", pricePerPerson: 49.5, privatePrice: null });
    expect(pricingBody({ bookingMode: "Both", pricePerPerson: "49", privatePrice: "390" })).toEqual({ bookingMode: "Both", pricePerPerson: 49, privatePrice: 390 });
  });
});

describe("price and season checks", () => {
  it("rejects zero, negatives and sub-cent prices with our own messages", () => {
    expect(priceProblem("")).toBeUndefined();
    expect(priceProblem("49.50")).toBeUndefined();
    expect(priceProblem("0")).toBe("Enter a price above 0, or leave it empty.");
    expect(priceProblem("12.345")).toBe("Use at most 2 decimals, e.g. 49.50.");
  });

  it("checks a season's dates and prices", () => {
    const season = { from: "2026-11-20", to: "2026-11-30", perPerson: "55", privately: "" };
    expect(seasonProblem(season)).toBeUndefined();
    expect(seasonProblem({ ...season, to: "" })).toBe("Pick the first and last day of the season.");
    expect(seasonProblem({ ...season, to: "2026-11-10" })).toBe("The last day must be on or after the first day.");
    expect(seasonProblem({ ...season, perPerson: "" })).toBe("Enter at least one seasonal price.");
    expect(seasonProblem({ ...season, privately: "1.999" })).toBe("Use at most 2 decimals, e.g. 49.50.");
  });
});

describe("meeting point", () => {
  it("needs a pinned point: a new tour has none until the city is picked", () => {
    const fresh = detailsSchema.safeParse({ ...detailsDefaults(), title: "Harbour Cruise", description: "A calm evening cruise.", city: "Split", address: "Pier 4" });
    expect(fresh.success).toBe(false);
    expect(fresh.error?.issues.map((issue) => issue.message)).toContain("Pick the city, then put the pin on the meeting point.");

    const pinned = detailsSchema.safeParse({ ...detailsDefaults(), title: "Harbour Cruise", description: "A calm evening cruise.", city: "Split", address: "Pier 4", latitude: 43.5081, longitude: 16.4402 });
    expect(pinned.success).toBe(true);
  });
});

describe("details", () => {
  it("nests the location and upper-cases the country", () => {
    const body = detailsBody({ ...detailsDefaults(), title: "Harbour Cruise", description: "A calm evening cruise.", city: "Split", address: "Pier 4", countryCode: "hr" });
    expect(body.location).toMatchObject({ countryCode: "HR", city: "Split", address: "Pier 4", timeZoneId: "Europe/Zagreb" });
  });
});

describe("policyProblems", () => {
  it("accepts a sensible policy", () => {
    expect(policyProblems([{ hoursBeforeStart: 72, refundPercent: 100 }, { hoursBeforeStart: 24, refundPercent: 50 }])).toEqual([]);
  });

  it("rejects duplicate hours and refunds that grow toward departure", () => {
    expect(policyProblems([{ hoursBeforeStart: 24, refundPercent: 50 }, { hoursBeforeStart: 24, refundPercent: 20 }])).toContain("Each tier needs different hours.");
    expect(policyProblems([{ hoursBeforeStart: 72, refundPercent: 50 }, { hoursBeforeStart: 24, refundPercent: 100 }])).toContain("Refunds can't grow closer to departure.");
  });
});
