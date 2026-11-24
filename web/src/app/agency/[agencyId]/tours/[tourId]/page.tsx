import type { Metadata } from "next";
import Link from "next/link";

import { PhotoManager } from "@/components/agency/photo-manager";
import { ScheduleRulesSection } from "@/components/agency/schedule-rules";
import { DetailsSection, PolicySection, PricingSection, Section, SeasonsSection, TourActions } from "@/components/agency/tour-editor";
import { TourStatusPill } from "@/components/agency/tour-status";
import { getAgencyTour, listVehicles, requireAgency } from "@/lib/agency";
import { todayIn } from "@/lib/format";

// Private, per-user back-office data: rendered on demand, never prerendered.
export const instant = false;

export const metadata: Metadata = { title: "Edit tour" };

type Props = { params: Promise<{ agencyId: string; tourId: string }> };

export default async function EditTourPage({ params }: Props) {
  const { agencyId, tourId } = await params;
  const agency = await requireAgency(agencyId, `/agency/${agencyId}/tours/${tourId}`);
  const [tour, vehicles] = await Promise.all([getAgencyTour(agencyId, tourId), listVehicles(agencyId)]);
  // Each form is keyed by its own fields only: saving one section refreshes the page with the API's data, which resets
  // that form, without discarding unsaved edits in the others.
  const key = (...parts: unknown[]) => JSON.stringify(parts);
  const detailsKey = key(tour.title, tour.description, tour.location, tour.durationMinutes, tour.maxGuests);
  const pricingKey = key(tour.bookingMode, tour.pricePerPerson, tour.privatePrice);
  const policyKey = key(tour.cancellationPolicy);

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <Link href={`/agency/${agencyId}/tours`} className="text-sm font-semibold text-primary hover:text-primary-hover">← Tours</Link>
          <div className="mt-2 flex flex-wrap items-center gap-3"><h1 className="text-[32px] font-bold">{tour.title}</h1><TourStatusPill status={tour.status} /></div>
        </div>
        <TourActions agencyId={agencyId} tour={tour} agencyStatus={agency.status} />
      </div>
      <Section id="photos-heading" title="Photos" description="The first photo leads the gallery; the cover is used on search cards.">
        <PhotoManager agencyId={agencyId} tourId={tour.id} photos={tour.photos} />
      </Section>
      <ScheduleRulesSection agencyId={agencyId} tour={tour} vehicles={vehicles} today={todayIn(tour.location.timeZoneId)} />
      <DetailsSection key={detailsKey} agencyId={agencyId} tour={tour} />
      <PricingSection key={pricingKey} agencyId={agencyId} tour={tour} />
      <SeasonsSection agencyId={agencyId} tour={tour} />
      <PolicySection key={policyKey} agencyId={agencyId} tour={tour} />
    </div>
  );
}
