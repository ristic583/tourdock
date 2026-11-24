"use client";

import { useDepartureSelection } from "@/components/catalog/departure-selection";
import type { RefundTier } from "@/lib/api/contracts";
import { policySegments, segmentLabel } from "@/lib/availability";
import { formatInZone } from "@/lib/format";
import { cn } from "@/lib/utils";

const HOUR = 60 * 60 * 1000;

function tone(refundPercent: number): string {
  if (refundPercent >= 100) return "bg-primary text-primary-foreground";
  if (refundPercent > 0) return "bg-primary-soft text-ink";
  return "bg-surface-2 text-ink";
}

/**
 * Refund windows as one bar, each part as wide as its window (see policySegments). Once a departure is picked,
 * the deadlines under the bar become real dates in the tour's time zone.
 */
export function CancellationPolicy({ tiers, timeZone }: { tiers: RefundTier[]; timeZone: string }) {
  const { departure, pickedAt } = useDepartureSelection();
  const segments = policySegments(tiers);
  const columns = segments.map((segment) => `${segment.weight}fr`).join(" ");
  const start = departure ? new Date(departure.startsAt).getTime() : undefined;
  const until = (hours: number) => {
    if (start === undefined) return undefined;
    const deadline = new Date(start - hours * HOUR);
    const when = formatInZone(deadline, timeZone);
    return pickedAt !== undefined && deadline.getTime() < pickedAt ? `${when.date} (passed)` : `until ${when.date}, ${when.time}`;
  };
  const startLabel = departure ? formatInZone(departure.startsAt, timeZone) : undefined;

  return (
    <section aria-labelledby="policy-heading">
      <h2 id="policy-heading" className="mb-1.5 text-2xl font-bold">Cancellation policy</h2>
      <p className="mb-4 text-muted-foreground">
        {startLabel
          ? `Refund depends on how long before departure you cancel. Dates below are for ${startLabel.date}, ${startLabel.time}.`
          : "Refund depends on how long before departure you cancel. Pick a departure to see the exact dates."}
      </p>
      {/* Phones: a thin proportional bar, then the windows as a list (narrow parts can't hold their text). */}
      <div className="mb-2 grid h-2 gap-0.5 overflow-hidden rounded-full sm:hidden" style={{ gridTemplateColumns: columns }} aria-hidden>
        {segments.map((segment) => <span key={segment.fromHours} className={tone(segment.refundPercent)} />)}
      </div>
      <ol className="grid gap-1 overflow-hidden rounded-control max-sm:grid-cols-1! max-sm:rounded-card" style={{ gridTemplateColumns: columns }}>
        {segments.map((segment) => (
          <li key={segment.fromHours} className={cn("min-w-0 px-3.5 py-3", tone(segment.refundPercent))}>
            <p className="font-bold">{segment.refundPercent > 0 ? `${segment.refundPercent}% refund` : "No refund"}</p>
            <p className={cn("text-[13px]", segment.refundPercent >= 100 ? "opacity-90" : "text-muted-foreground")}>{segmentLabel(segment)}</p>
            {start !== undefined && (
              <p className={cn("text-[13px] sm:hidden", segment.refundPercent >= 100 ? "opacity-90" : "text-muted-foreground")}>
                {segment.fromHours > 0 ? until(segment.fromHours) : "until departure"}
              </p>
            )}
          </li>
        ))}
      </ol>
      {start !== undefined && (
        <div className="mt-2 grid gap-1 text-[13px] text-muted-foreground max-sm:hidden" style={{ gridTemplateColumns: columns }}>
          {segments.map((segment) => (
            <span key={segment.fromHours} className="min-w-0">{segment.fromHours > 0 ? until(segment.fromHours) : "departure"}</span>
          ))}
        </div>
      )}
    </section>
  );
}
