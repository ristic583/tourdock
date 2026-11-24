import { MapPin, Star } from "lucide-react";
import Link from "next/link";

import type { CitedTour, NextDeparture, TourSummary } from "@/lib/api/contracts";
import { seats } from "@/lib/availability";
import { tourImage } from "@/lib/demo-media";
import { formatDuration, formatLocal, formatMoney, priceUnit } from "@/lib/format";
import { cn } from "@/lib/utils";

/**
 * A tour the assistant recommends: photo, place, length, rating and price, then its next departures with free seats.
 * A date opens the tour page on that day. Chats saved before cards existed (or a tour that is gone) show just the link.
 */
export function ChatTourCard({ tour }: { tour: CitedTour }) {
  const details = tour.details ?? null;
  if (!details) {
    return (
      <Link href={`/tours/${tour.id}`} className="flex items-center gap-2 rounded-control border border-line px-3 py-2 font-medium hover:bg-surface-2">
        <MapPin className="size-4 flex-none text-primary" aria-hidden />
        <span className="truncate">{tour.title}</span>
      </Link>
    );
  }

  return (
    <article className="overflow-hidden rounded-card border border-line bg-surface">
      <Summary tourId={tour.id} details={details} />
      <Departures tourId={tour.id} title={details.title} departures={tour.departures ?? []} />
    </article>
  );
}

function Summary({ tourId, details }: { tourId: string; details: TourSummary }) {
  const price = details.pricePerPerson ?? details.privatePrice;
  const image = tourImage(details.title, details.coverPhotoUrl);
  return (
    <Link href={`/tours/${tourId}`} className="flex gap-3 p-2.5 hover:bg-surface-2">
      <div className={cn("relative size-16 flex-none overflow-hidden rounded-control", !image && "photo-sea")}>
        {image && (
          // Same source as the search cards: the agency's object-storage URL (see TourCard).
          // eslint-disable-next-line @next/next/no-img-element
          <img src={image} alt="" loading="lazy" className="absolute inset-0 size-full object-cover" />
        )}
      </div>
      <div className="min-w-0 flex-1">
        <h3 className="line-clamp-2 leading-tight font-semibold">{details.title}</h3>
        <p className="mt-0.5 flex items-center gap-1 truncate text-xs text-muted-foreground">
          <span className="truncate">
            {details.city}, {details.countryCode} · {formatDuration(details.durationMinutes)}
          </span>
          {details.averageRating !== null && (
            <span className="flex flex-none items-center gap-0.5 font-semibold text-ink">
              · <Star className="size-3 fill-star text-star" aria-hidden />
              <span className="sr-only">Rated</span>
              {details.averageRating.toFixed(1)}
            </span>
          )}
        </p>
        <p className="mt-1 text-xs text-muted-foreground">
          {price ? (
            <>
              from <span className="text-sm font-bold text-ink">{formatMoney(price)}</span> {priceUnit(Boolean(details.pricePerPerson))}
            </>
          ) : (
            "Price on request"
          )}
        </p>
      </div>
    </Link>
  );
}

function Departures({ tourId, title, departures }: { tourId: string; title: string; departures: NextDeparture[] }) {
  if (departures.length === 0) {
    return <p className="border-t border-line px-2.5 py-2 text-xs text-muted-foreground">No free seats in the coming months.</p>;
  }

  return (
    <ul aria-label={`Next departures of ${title}`} className="flex flex-wrap gap-1.5 border-t border-line px-2.5 py-2">
      {departures.map((departure) => {
        const when = formatLocal(departure.localStart);
        const seat = seats(departure.remainingSeats, departure.capacity);
        return (
          <li key={departure.id}>
            <Link
              href={`/tours/${tourId}?date=${departure.localStart.slice(0, 10)}`}
              className="inline-flex items-center gap-1 rounded-full border border-line px-2.5 py-1 text-xs hover:border-primary hover:bg-primary-soft"
            >
              <span className="font-medium">{when.date}</span>
              <span className="text-muted-foreground">{when.time}</span>
              <span className={cn("text-muted-foreground", seat.urgent && "font-semibold text-urgent-ink")}>
                · {seat.left === 1 ? "1 seat" : `${seat.left} seats`}
              </span>
            </Link>
          </li>
        );
      })}
    </ul>
  );
}
