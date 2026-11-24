import type { AgencyStats, Money, ScheduleRule, Slot } from "@/lib/api/contracts";
import { formatPickedDate } from "@/lib/calendar-dates";
import { addDays } from "@/lib/format";

/** An inclusive range of local departure dates (YYYY-MM-DD), as `GET /agencies/{id}/stats` takes it. */
export type DateRange = { from: string; to: string };

/** The ranges the overview asks the stats endpoint for: this calendar month, the next 7 days and the next 30 days. */
export function overviewRanges(today: string): { month: DateRange; week: DateRange; next30: DateRange } {
  const monthStart = `${today.slice(0, 8)}01`;
  const nextMonth = new Date(`${monthStart}T00:00:00Z`);
  nextMonth.setUTCMonth(nextMonth.getUTCMonth() + 1);
  return {
    month: { from: monthStart, to: addDays(nextMonth.toISOString().slice(0, 10), -1) },
    week: { from: today, to: addDays(today, 6) },
    next30: { from: today, to: addDays(today, 29) },
  };
}

/** KPIs for the agency overview. */
export type AgencyKpis = {
  departuresNext7Days: number;
  vehiclesInUse: number;
  seatsSold: number;
  seatsOnSale: number;
  occupancyPercent: number;
  revenueThisMonth: Money;
  refundedThisMonth: Money | null;
  guestsToday: number;
  departuresToday: number;
  pendingHolds: number;
};

/**
 * Picks the overview's numbers from three stats responses (see `overviewRanges`). Revenue is what was paid for bookings
 * departing this month, net of refunds. "Today" and pending holds don't depend on the range.
 */
export function agencyKpis({ month, week, next30 }: { month: AgencyStats; week: AgencyStats; next30: AgencyStats }): AgencyKpis {
  return {
    departuresNext7Days: week.departures,
    vehiclesInUse: week.vehiclesInUse,
    seatsSold: next30.seatsSold,
    seatsOnSale: next30.seatsOnSale,
    occupancyPercent: next30.occupancyPercent,
    revenueThisMonth: month.revenue.net,
    refundedThisMonth: month.revenue.refunded.amount > 0 ? month.revenue.refunded : null,
    guestsToday: week.today.guests,
    departuresToday: week.today.departures,
    pendingHolds: week.pendingHolds,
  };
}

export const WEEKDAYS = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"] as const;
export type Weekday = (typeof WEEKDAYS)[number];

/** The API's DaysOfWeek flags enum travels as "Monday, Wednesday". */
export function parseDays(value: string): Weekday[] {
  const names = value.split(",").map((part) => part.trim());
  return WEEKDAYS.filter((day) => names.includes(day));
}

export function formatDays(days: readonly Weekday[]): string {
  return WEEKDAYS.filter((day) => days.includes(day)).join(", ");
}

/** Monday-based weekday of a YYYY-MM-DD date. */
export function weekdayOf(date: string): Weekday {
  const index = (new Date(`${date}T00:00:00Z`).getUTCDay() + 6) % 7;
  return WEEKDAYS[index];
}

/** The Monday on or before a date. */
export function startOfWeek(date: string): string {
  return addDays(date, -WEEKDAYS.indexOf(weekdayOf(date)));
}

/** How many departures a rule would create between two dates (inclusive), before conflicts are skipped. */
export function countRuleDepartures(days: readonly Weekday[], from: string, until: string): number {
  if (!days.length || until < from) return 0;
  let count = 0;
  for (let date = from; date <= until; date = addDays(date, 1)) if (days.includes(weekdayOf(date))) count++;
  return count;
}

export function describeRule(rule: Pick<ScheduleRule, "days" | "startTime" | "validFrom" | "validUntil">): string {
  const days = parseDays(rule.days).map((day) => day.slice(0, 3)).join(", ");
  const until = rule.validUntil ? ` until ${formatPickedDate(rule.validUntil)}` : "";
  return `Every ${days || "—"} at ${rule.startTime.slice(0, 5)}, from ${formatPickedDate(rule.validFrom)}${until}`;
}

/** Chip style for a departure in the calendar. */
export function slotKind(slot: Slot): "blocked" | "cancelled" | "private" | "full" | "shared" {
  if (slot.status === "Blocked") return "blocked";
  if (slot.status === "Cancelled") return "cancelled";
  if (slot.isPrivatelyBooked) return "private";
  return slot.remainingSeats <= 0 ? "full" : "shared";
}
