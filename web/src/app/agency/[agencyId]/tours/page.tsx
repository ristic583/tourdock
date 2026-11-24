import type { Metadata } from "next";
import Link from "next/link";

import { TourStatusPill } from "@/components/agency/tour-status";
import { Button } from "@/components/ui/button";
import { listAgencyTours, requireAgency } from "@/lib/agency";
import { bookingModeLabel, formatDuration, formatMoney } from "@/lib/format";

// Private, per-user back-office data: rendered on demand, never prerendered.
export const instant = false;

export const metadata: Metadata = { title: "Tours" };

type Props = { params: Promise<{ agencyId: string }> };

export default async function AgencyToursPage({ params }: Props) {
  const { agencyId } = await params;
  await requireAgency(agencyId, `/agency/${agencyId}/tours`);
  const tours = await listAgencyTours(agencyId);
  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-[32px] font-bold">Tours</h1>
          <p className="text-muted-foreground">Drafts are only visible to you. Publish a tour to put its departures on sale.</p>
        </div>
        <Button asChild><Link href={`/agency/${agencyId}/tours/new`}>+ New tour</Link></Button>
      </div>
      {tours.length ? (
        <ul className="grid gap-4 sm:grid-cols-[repeat(auto-fill,minmax(300px,1fr))]">
          {tours.map((tour) => {
            const cover = tour.photos.find((photo) => photo.isCover) ?? tour.photos[0];
            return (
              <li key={tour.id}>
                <Link href={`/agency/${agencyId}/tours/${tour.id}`} className="flex h-full flex-col overflow-hidden rounded-card border border-line bg-surface text-ink hover:border-muted-foreground">
                  <div className={`relative h-32 ${cover ? "" : "photo-sea"}`}>
                    {/* eslint-disable-next-line @next/next/no-img-element */}
                    {cover && <img src={cover.urls.thumbnail} alt="" className="absolute inset-0 size-full object-cover" />}
                  </div>
                  <div className="flex flex-1 flex-col gap-1 p-4">
                    <div className="flex items-start justify-between gap-2"><h2 className="text-lg font-bold">{tour.title}</h2><TourStatusPill status={tour.status} /></div>
                    <p className="text-sm text-muted-foreground">{tour.location.city} · {formatDuration(tour.durationMinutes)} · up to {tour.maxGuests}</p>
                    <p className="text-sm">{bookingModeLabel(tour.bookingMode)} · {[tour.pricePerPerson && `${formatMoney(tour.pricePerPerson)} pp`, tour.privatePrice && `${formatMoney(tour.privatePrice)} trip`].filter(Boolean).join(" · ")}</p>
                    <p className="text-xs text-muted-foreground">{tour.photos.length} photos · {tour.scheduleRules.filter((rule) => rule.isActive).length} active schedules</p>
                  </div>
                </Link>
              </li>
            );
          })}
        </ul>
      ) : (
        <p className="rounded-card border border-dashed border-line bg-surface p-6 text-muted-foreground">No tours yet.</p>
      )}
    </div>
  );
}
