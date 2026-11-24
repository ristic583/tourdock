import type { ExchangeRates, Money } from "@/lib/api/contracts";

/** Currencies a traveler can see prices in. Bookings are always charged in EUR (ADR 0017). */
export const BASE_CURRENCY = "EUR";
export const DISPLAY_CURRENCIES = ["EUR", "USD", "GBP", "CHF", "SEK", "NOK", "DKK", "PLN", "CZK", "HUF", "RON"] as const;
export type DisplayCurrency = (typeof DISPLAY_CURRENCIES)[number];

/** Cookie holding the chosen display currency, so server-rendered prices are right on first paint. */
export const CURRENCY_COOKIE = "td-currency";

export function parseCurrency(value: string | undefined | null): DisplayCurrency {
  const code = value?.trim().toUpperCase();
  return (DISPLAY_CURRENCIES as readonly string[]).includes(code ?? "") ? (code as DisplayCurrency) : BASE_CURRENCY;
}

/** A rate to show approximate prices with, or null to show EUR only. */
export type DisplayRate = { currency: DisplayCurrency; rate: number; date: string; stale: boolean };

/** Null when the base currency is chosen or rates are missing: the UI then simply shows EUR. */
export function displayRate(currency: DisplayCurrency, rates: ExchangeRates | null | undefined): DisplayRate | null {
  if (currency === BASE_CURRENCY || !rates?.available || !rates.date) return null;
  const rate = rates.rates[currency];
  return typeof rate === "number" && rate > 0 ? { currency, rate, date: rates.date, stale: rates.isStale } : null;
}

/** Converts a EUR amount, rounded to cents half away from zero (the API rounds the same way). */
export function convert(money: Money | null | undefined, rate: DisplayRate | null): Money | null {
  if (!money || !rate || money.currency !== BASE_CURRENCY) return null;
  const cents = Math.round(Math.abs(money.amount) * rate.rate * 100 + Number.EPSILON);
  return { amount: (Math.sign(money.amount) * cents) / 100, currency: rate.currency };
}

/** "6 Nov" for the rates note. */
export function ratesDateLabel(date: string): string {
  return new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", timeZone: "UTC" }).format(new Date(`${date}T00:00:00Z`));
}
