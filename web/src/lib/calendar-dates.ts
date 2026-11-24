/**
 * Calendar maths on plain "YYYY-MM-DD" dates. Everything runs in UTC so a date never shifts with the viewer's time
 * zone or a DST change: these are calendar days at the tour, not instants.
 */

export type IsoDate = string;

const ISO = /^\d{4}-\d{2}-\d{2}$/;

export function isIsoDate(value: string | undefined | null): value is IsoDate {
  if (!value || !ISO.test(value)) return false;
  return toIso(fromIso(value)) === value; // rejects 2026-02-31
}

export function fromIso(value: IsoDate): Date {
  const [year, month, day] = value.split("-").map(Number);
  return new Date(Date.UTC(year, month - 1, day));
}

export function toIso(date: Date): IsoDate {
  return date.toISOString().slice(0, 10);
}

export function addDays(value: IsoDate, days: number): IsoDate {
  const date = fromIso(value);
  date.setUTCDate(date.getUTCDate() + days);
  return toIso(date);
}

/** First day of the month `months` away from the month containing `value`. */
export function addMonths(value: IsoDate, months: number): IsoDate {
  const date = fromIso(value);
  return toIso(new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + months, 1)));
}

export function startOfMonth(value: IsoDate): IsoDate {
  return `${value.slice(0, 7)}-01`;
}

/** Whether `value` lies within the optional inclusive bounds. ISO dates compare correctly as strings. */
export function inRange(value: IsoDate, min?: IsoDate, max?: IsoDate): boolean {
  return (!min || value >= min) && (!max || value <= max);
}

/** The 6×7 grid of days shown for a month, weeks starting on Monday, with days of the neighbouring months. */
export function monthGrid(month: IsoDate): IsoDate[] {
  const first = fromIso(startOfMonth(month));
  const mondayOffset = (first.getUTCDay() + 6) % 7;
  const start = addDays(toIso(first), -mondayOffset);
  return Array.from({ length: 42 }, (_, index) => addDays(start, index));
}

const long = new Intl.DateTimeFormat("en-GB", { weekday: "short", day: "numeric", month: "short", year: "numeric", timeZone: "UTC" });
const monthTitle = new Intl.DateTimeFormat("en-GB", { month: "long", year: "numeric", timeZone: "UTC" });
const fullDay = new Intl.DateTimeFormat("en-GB", { weekday: "long", day: "numeric", month: "long", year: "numeric", timeZone: "UTC" });

/** "Sat 14 Nov 2026" — what the picker shows instead of the browser's "dd. mm. yyyy." placeholder. */
export function formatPickedDate(value: IsoDate): string {
  return long.format(fromIso(value)).replace(",", "");
}

export function formatMonthTitle(value: IsoDate): string {
  return monthTitle.format(fromIso(value));
}

/** Accessible name of a day button, e.g. "Saturday 14 November 2026". */
export function formatDayLabel(value: IsoDate): string {
  return fullDay.format(fromIso(value)).replace(",", "");
}

/** Today's calendar date in a time zone (the viewer's when omitted). */
export function todayIso(timeZone?: string): IsoDate {
  return new Intl.DateTimeFormat("en-CA", { year: "numeric", month: "2-digit", day: "2-digit", timeZone }).format(new Date());
}
