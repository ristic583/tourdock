import type { Departure, RefundTier } from "@/lib/api/contracts";

/** At or below this many seats left, availability is shown as urgent (orange). */
export const FEW_SEATS = 3;

export type Seats = { left: number; capacity: number; label: string; filledPercent: number; urgent: boolean };

export function seats(left: number, capacity: number): Seats {
  const safeCapacity = Math.max(capacity, 1);
  return {
    left,
    capacity,
    label: `${left} of ${capacity} left`,
    filledPercent: Math.round(((safeCapacity - Math.min(left, safeCapacity)) / safeCapacity) * 100),
    urgent: left > 0 && left <= FEW_SEATS,
  };
}

/** The first departure the given party can actually book (seats or the whole trip). */
export function firstBookable(departures: Departure[]): Departure | undefined {
  return departures.find((departure) => departure.canBookSeats || departure.canBookPrivately);
}

export type PolicySegment = {
  refundPercent: number;
  /** Cancel at least this many hours before departure… */
  fromHours: number;
  /** …and less than this many (null: no upper bound). */
  toHours: number | null;
  /** Relative width in the bar. */
  weight: number;
};

/**
 * Turns refund tiers into consecutive time windows, earliest first, ending with the window up to departure.
 * Widths follow the length of each window; the open-ended first window is drawn as long as its own threshold,
 * so the usual 72h / 24h policy reads 3 : 2 : 1. A "no refund" window is added when the policy has no 0h tier.
 */
export function policySegments(tiers: RefundTier[]): PolicySegment[] {
  const sorted = [...tiers].sort((a, b) => b.hoursBeforeStart - a.hoursBeforeStart);
  if (!sorted.length) return [{ refundPercent: 0, fromHours: 0, toHours: null, weight: 1 }];
  if (sorted.at(-1)!.hoursBeforeStart > 0) sorted.push({ hoursBeforeStart: 0, refundPercent: 0 });

  const segments = sorted.map((tier, index): PolicySegment => {
    const toHours = index === 0 ? null : sorted[index - 1].hoursBeforeStart;
    const span = toHours === null ? tier.hoursBeforeStart : toHours - tier.hoursBeforeStart;
    return { refundPercent: tier.refundPercent, fromHours: tier.hoursBeforeStart, toHours, weight: span };
  });
  // Keep every segment readable, however short its window.
  const total = segments.reduce((sum, segment) => sum + segment.weight, 0) || 1;
  return segments.map((segment) => ({ ...segment, weight: Math.max(segment.weight / total, 0.12) }));
}

export function hoursLabel(hours: number): string {
  return hours % 24 === 0 && hours >= 48 ? `${hours / 24} days` : `${hours}h`;
}

export function segmentLabel(segment: PolicySegment): string {
  if (segment.toHours === null) return segment.fromHours === 0 ? "any time" : `${hoursLabel(segment.fromHours)} or more before`;
  if (segment.fromHours === 0) return `under ${hoursLabel(segment.toHours)}`;
  return `${hoursLabel(segment.fromHours)}–${hoursLabel(segment.toHours)} before`;
}

/** The latest moment a full refund is still possible, in hours before departure (undefined: never free). */
export function freeCancellationHours(tiers: RefundTier[]): number | undefined {
  const full = tiers.filter((tier) => tier.refundPercent >= 100).map((tier) => tier.hoursBeforeStart);
  return full.length ? Math.min(...full) : undefined;
}

/**
 * The refund percent the policy gives when cancelling `hoursBeforeStart` hours before departure: the tier with the
 * largest notice that is still met, 0 when none is (or after the start).
 */
export function refundPercentAt(tiers: RefundTier[], hoursBeforeStart: number): number {
  if (hoursBeforeStart <= 0) return 0;
  const met = tiers.filter((tier) => tier.hoursBeforeStart <= hoursBeforeStart).sort((a, b) => b.hoursBeforeStart - a.hoursBeforeStart);
  return met[0]?.refundPercent ?? 0;
}
