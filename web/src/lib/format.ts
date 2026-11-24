import type { BookingMode, Money } from "@/lib/api/contracts";

export function formatMoney(money: Money | null, fallback = "Price on request"): string {
  if (!money) return fallback;
  // Whole amounts without decimals (€49), anything else with cents (€49.50).
  const digits = Number.isInteger(money.amount) ? 0 : 2;
  return new Intl.NumberFormat("en", { style: "currency", currency: money.currency, minimumFractionDigits: digits, maximumFractionDigits: digits }).format(money.amount);
}

export function formatDuration(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  const remainder = minutes % 60;
  return [hours ? `${hours}h` : undefined, remainder ? `${remainder}m` : undefined].filter(Boolean).join(" ");
}

export function bookingModeLabel(mode: BookingMode): string {
  return mode === "Both" ? "Shared or private" : mode === "Shared" ? "Shared trip" : "Private trip";
}

/**
 * Formats a wall-clock time at the tour's location (`Departure.localStart`, e.g. "2027-06-05T09:00:00")
 * exactly as written, without converting it to the viewer's time zone.
 */
export function formatLocal(localStart: string): { date: string; weekday: string; day: string; time: string } {
  const date = new Date(`${localStart.slice(0, 19)}Z`);
  const part = (options: Intl.DateTimeFormatOptions) => new Intl.DateTimeFormat("en-GB", { ...options, timeZone: "UTC" }).format(date);
  return {
    date: part({ weekday: "short", day: "numeric", month: "short" }),
    weekday: part({ weekday: "short" }),
    day: part({ day: "numeric" }),
    time: part({ hour: "2-digit", minute: "2-digit", hourCycle: "h23" }),
  };
}

/** "/ person" for shared prices, "/ trip" for private ones. */
export function priceUnit(perPerson: boolean): string {
  return perPerson ? "/ person" : "/ trip";
}

/** Today's date (YYYY-MM-DD) at a location. */
export function todayIn(timeZone: string, now: Date = new Date()): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone, year: "numeric", month: "2-digit", day: "2-digit" }).format(now);
}

/** Adds days to a YYYY-MM-DD date. */
export function addDays(date: string, days: number): string {
  const value = new Date(`${date}T00:00:00Z`);
  value.setUTCDate(value.getUTCDate() + days);
  return value.toISOString().slice(0, 10);
}

/** A moment shown as a 24-hour time and short date in the tour's time zone, e.g. "Thu 11 Jun, 18:00". */
export function formatInZone(value: string | Date, timeZone: string): { date: string; time: string } {
  const moment = typeof value === "string" ? new Date(value) : value;
  return {
    date: new Intl.DateTimeFormat("en-GB", { weekday: "short", day: "numeric", month: "short", timeZone }).format(moment),
    time: new Intl.DateTimeFormat("en-GB", { hour: "2-digit", minute: "2-digit", hourCycle: "h23", timeZone }).format(moment),
  };
}
