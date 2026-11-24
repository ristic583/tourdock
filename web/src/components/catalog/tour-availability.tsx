import type { NextDeparture } from "@/lib/api/contracts";
import { seats } from "@/lib/availability";
import { formatLocal } from "@/lib/format";
import { cn } from "@/lib/utils";

/**
 * "N of M left" with a thin fill bar, for the soonest departure that fits the search. The search result carries it with
 * live seat counts; `null` means it sold out since the result list was cached.
 */
export function TourAvailability({ next }: { next: NextDeparture | null }) {
  if (!next) return <p className="text-[13px] font-semibold text-urgent-ink">Sold out for these dates</p>;

  const seat = seats(next.remainingSeats, next.capacity);
  const when = formatLocal(next.localStart);
  return (
    <div className="text-[13px]">
      <div className="flex items-baseline justify-between gap-2 whitespace-nowrap">
        <span className="truncate text-muted-foreground">{when.date} · {when.time}</span>
        <span className={cn("font-semibold", seat.urgent && "text-urgent-ink")}>{seat.label}</span>
      </div>
      <span className="mt-1.5 block h-1 overflow-hidden rounded-full bg-surface-2" aria-hidden>
        <span className="block h-full rounded-full bg-primary" style={{ width: `${seat.filledPercent}%` }} />
      </span>
    </div>
  );
}

/** Orange badge on the photo when the next departure is nearly full. */
export function UrgencyBadge({ next }: { next: NextDeparture | null }) {
  if (!next) return null;
  const seat = seats(next.remainingSeats, next.capacity);
  if (!seat.urgent) return null;
  return (
    <span className="absolute bottom-3 left-3 inline-flex h-6 items-center rounded-full bg-urgent-soft px-2.5 text-xs font-semibold text-urgent-ink">
      {seat.left === 1 ? "1 seat left" : `${seat.left} seats left`} · {formatLocal(next.localStart).weekday}
    </span>
  );
}
