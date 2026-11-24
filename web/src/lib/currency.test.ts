import { describe, expect, it } from "vitest";

import type { ExchangeRates } from "@/lib/api/contracts";

import { convert, displayRate, parseCurrency, ratesDateLabel, type DisplayRate } from "./currency";

const rates: ExchangeRates = { baseCurrency: "EUR", available: true, date: "2026-11-06", isStale: false, rates: { USD: 1.1165, HUF: 363.9 } };
const usd: DisplayRate = { currency: "USD", rate: 1.1165, date: "2026-11-06", stale: false };

describe("parseCurrency", () => {
  it("accepts supported codes in any case and falls back to EUR", () => {
    expect(parseCurrency("usd")).toBe("USD");
    expect(parseCurrency(" GBP ")).toBe("GBP");
    expect(parseCurrency("JPY")).toBe("EUR");
    expect(parseCurrency("<script>")).toBe("EUR");
    expect(parseCurrency(undefined)).toBe("EUR");
  });
});

describe("displayRate", () => {
  it("picks the rate for the chosen currency", () => {
    expect(displayRate("USD", rates)).toEqual(usd);
    expect(displayRate("HUF", { ...rates, isStale: true })).toEqual({ currency: "HUF", rate: 363.9, date: "2026-11-06", stale: true });
  });

  it("shows EUR only when EUR is chosen, rates are missing or unavailable", () => {
    expect(displayRate("EUR", rates)).toBeNull();
    expect(displayRate("GBP", rates)).toBeNull();
    expect(displayRate("USD", null)).toBeNull();
    expect(displayRate("USD", { ...rates, available: false, date: null, rates: {} })).toBeNull();
  });
});

describe("convert", () => {
  it("converts EUR and rounds to cents like the API", () => {
    expect(convert({ amount: 45, currency: "EUR" }, usd)).toEqual({ amount: 50.24, currency: "USD" });
    expect(convert({ amount: 420, currency: "EUR" }, usd)).toEqual({ amount: 468.93, currency: "USD" });
    expect(convert({ amount: 50, currency: "EUR" }, { ...usd, rate: 1.0825 })).toEqual({ amount: 54.13, currency: "USD" }); // 54.125 rounds up
  });

  it("returns nothing without a rate or for non-EUR amounts", () => {
    expect(convert({ amount: 45, currency: "EUR" }, null)).toBeNull();
    expect(convert(null, usd)).toBeNull();
    expect(convert({ amount: 45, currency: "USD" }, usd)).toBeNull();
  });
});

describe("ratesDateLabel", () => {
  it("formats the ECB date without shifting time zones", () => {
    expect(ratesDateLabel("2026-11-06")).toBe("6 Nov");
  });
});
