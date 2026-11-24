import { Star } from "lucide-react";
import Link from "next/link";

import { ApproxPrice } from "@/components/catalog/approx-price";
import { TourAvailability, UrgencyBadge } from "@/components/catalog/tour-availability";
import type { TourSummary } from "@/lib/api/contracts";
import type { DisplayRate } from "@/lib/currency";
import { tourImage } from "@/lib/demo-media";
import { formatDuration, formatMoney, priceUnit } from "@/lib/format";

const PLACEHOLDERS = ["photo-sea", "photo-sunset", "photo-island", "photo-hills", "photo-deep", "photo-desert"];

function Badge({ children }: { children: string }) {
  return <span className="inline-flex h-6 items-center rounded-full bg-white/92 px-2.5 text-xs font-semibold text-[#0f1a20]">{children}</span>;
}

/**
 * A search result. Price reads "from €X / person" (or "/ trip" for private-only tours), plus "≈ $Y" in the traveler's
 * display currency when one is chosen. Seats are for the soonest departure that fits the search.
 */
/** "800 m away" up close, "12 km away" further out. */
export function formatDistance(km: number): string {
  return km < 1 ? `${Math.max(100, Math.round((km * 1000) / 100) * 100)} m away` : `${Math.round(km)} km away`;
}

export function TourCard({ tour, index = 0, guests = 1, date, rate }: { tour: TourSummary; index?: number; guests?: number; date?: string; rate?: DisplayRate | null }) {
  const perPerson = Boolean(tour.pricePerPerson);
  const price = tour.pricePerPerson ?? tour.privatePrice;
  const image = tourImage(tour.title, tour.coverPhotoUrl);
  const shared = tour.bookingMode !== "Private";
  const privately = tour.bookingMode !== "Shared";
  return (
    <Link
      href={guests > 1 || date ? `/tours/${tour.id}?${new URLSearchParams({ ...(guests > 1 ? { guests: String(guests) } : {}), ...(date ? { date } : {}) })}` : `/tours/${tour.id}`}
      className="group flex flex-col overflow-hidden rounded-card border border-line bg-surface text-ink transition-shadow hover:shadow-card focus-visible:outline-offset-[-3px]"
    >
      <div className={`relative h-[190px] ${image ? "" : PLACEHOLDERS[index % PLACEHOLDERS.length]}`}>
        {image && (
          // Tour photos come from the agency's object-storage public URL. Plain <img> until a production CDN
          // hostname is known and can be allow-listed for next/image.
          // eslint-disable-next-line @next/next/no-img-element
          <img src={image} alt="" loading="lazy" className="absolute inset-0 size-full object-cover" />
        )}
        <div className="absolute top-3 left-3 flex gap-1.5">
          {shared && <Badge>Shared</Badge>}
          {privately && <Badge>Private</Badge>}
        </div>
        <UrgencyBadge next={tour.nextDeparture} />
      </div>
      <div className="flex flex-1 flex-col gap-1.5 px-[18px] pt-4 pb-[18px]">
        <div className="flex justify-between gap-2 text-[13px]">
          <span className="text-muted-foreground">
            {tour.city}, {tour.countryCode}
            {tour.distanceKm !== null && tour.distanceKm !== undefined && <> · {formatDistance(tour.distanceKm)}</>}
          </span>
          {tour.averageRating !== null && (
            <span className="flex items-center gap-1 font-semibold">
              <Star className="size-3.5 fill-star text-star" aria-hidden />
              <span className="sr-only">Rated</span>{tour.averageRating.toFixed(1)}
              <span className="font-normal text-muted-foreground">({tour.reviewCount})</span>
            </span>
          )}
        </div>
        <h3 className="text-lg leading-tight font-bold group-hover:text-primary">{tour.title}</h3>
        <div className="flex gap-3.5 text-[13px] text-muted-foreground">
          <span>{formatDuration(tour.durationMinutes)}</span>
          <span>Up to {tour.maxGuests} guests</span>
        </div>
        <div className="mt-1 min-h-5">
          <TourAvailability next={tour.nextDeparture} />
        </div>
        <div className="flex-1" />
        <div className="mt-2 flex items-baseline gap-1.5 border-t border-line pt-3">
          {price ? (
            <>
              <span className="text-[13px] text-muted-foreground">from</span>
              <span className="font-display text-[22px] font-bold">{formatMoney(price)}</span>
              <span className="text-[13px] text-muted-foreground">{priceUnit(perPerson)}</span>
              <ApproxPrice money={price} rate={rate} className="ml-auto" />
            </>
          ) : (
            <span className="text-sm text-muted-foreground">Price on request</span>
          )}
        </div>
      </div>
    </Link>
  );
}
