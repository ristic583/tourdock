import type { Metadata } from "next";

import { AddDepartureForm } from "@/components/agency/add-departure-form";
import { DepartureCalendar } from "@/components/agency/departure-calendar";
import { agencyTimeZone, listAgencyTours, listSlots, listVehicles, requireAgency } from "@/lib/agency";
import { startOfWeek } from "@/lib/agency-stats";
import { addDays, todayIn } from "@/lib/format";

// Private, per-user back-office data: rendered on demand, never prerendered.
export const instant = false;

export const metadata: Metadata = { title: "Departures calendar" };

type Props = { params: Promise<{ agencyId: string }>; searchParams: Promise<{ week?: string }> };

export default async function CalendarPage({ params, searchParams }: Props) {
  const { agencyId } = await params;
  await requireAgency(agencyId, `/agency/${agencyId}/calendar`);
  const [vehicles, tours] = await Promise.all([listVehicles(agencyId), listAgencyTours(agencyId)]);
  const today = todayIn(agencyTimeZone(tours));
  const requested = (await searchParams).week;
  const weekStart = startOfWeek(requested && /^\d{4}-\d{2}-\d{2}$/.test(requested) ? requested : today);
  const slots = await listSlots(agencyId, weekStart, addDays(weekStart, 6));
  const calendarTours = tours.map((tour) => ({ id: tour.id, title: tour.title, timeZoneId: tour.location.timeZoneId }));

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-[32px] font-bold">Calendar</h1>
        <p className="text-muted-foreground">Departures per vehicle. Pick one to block, unblock or cancel it. Times are each tour&apos;s local time.</p>
      </div>
      <DepartureCalendar agencyId={agencyId} vehicles={vehicles} tours={calendarTours} slots={slots} weekStart={weekStart} today={today} navigateTo={`/agency/${agencyId}/calendar`} />
      <AddDepartureForm agencyId={agencyId} tours={calendarTours} vehicles={vehicles} defaultDate={addDays(today, 1)} />
    </div>
  );
}
