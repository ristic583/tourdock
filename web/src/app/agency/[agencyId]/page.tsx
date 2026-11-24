import type { Metadata } from "next";
import Link from "next/link";

import { DepartureCalendar } from "@/components/agency/departure-calendar";
import { TourStatusPill } from "@/components/agency/tour-status";
import { Button } from "@/components/ui/button";
import { agencyTimeZone, getAgencyStats, listAgencyTours, listSlots, listVehicles, requireAgency } from "@/lib/agency";
import { agencyKpis, overviewRanges, startOfWeek } from "@/lib/agency-stats";
import { addDays, bookingModeLabel, formatDuration, formatInZone, formatLocal, formatMoney, todayIn } from "@/lib/format";

// Private, per-user back-office data: rendered on demand, never prerendered.
export const instant = false;

export const metadata: Metadata = { title: "Agency overview" };

type Props = { params: Promise<{ agencyId: string }> };

export default async function AgencyOverviewPage({ params }: Props) {
  const { agencyId } = await params;
  const agency = await requireAgency(agencyId, `/agency/${agencyId}`);
  const [vehicles, tours] = await Promise.all([listVehicles(agencyId), listAgencyTours(agencyId)]);
  const timeZone = agencyTimeZone(tours);
  const today = todayIn(timeZone);
  const weekStart = startOfWeek(today);
  const ranges = overviewRanges(today);
  // Slots only for the calendar week and each tour's next departure; the KPIs come from the stats endpoint.
  const [slots, month, week, next30] = await Promise.all([
    listSlots(agencyId, weekStart, ranges.next30.to),
    getAgencyStats(agencyId, ranges.month.from, ranges.month.to),
    getAgencyStats(agencyId, ranges.week.from, ranges.week.to),
    getAgencyStats(agencyId, ranges.next30.from, ranges.next30.to),
  ]);
  const kpis = agencyKpis({ month, week, next30 });
  const todayLabel = formatLocal(`${today}T00:00:00`);
  const calendarTours = tours.map((tour) => ({ id: tour.id, title: tour.title, timeZoneId: tour.location.timeZoneId }));

  return (
    <div className="flex flex-col gap-7">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <p className="text-sm text-muted-foreground">{todayLabel.date} · {timeZone}</p>
          <h1 className="text-[32px] font-bold">{agency.name}</h1>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button asChild variant="outline"><Link href={`/agency/${agencyId}/vehicles`}>Vehicles</Link></Button>
          <Button asChild><Link href={`/agency/${agencyId}/tours/new`}>+ New tour</Link></Button>
        </div>
      </div>

      <section aria-label="Key numbers" className="grid grid-cols-[repeat(auto-fit,minmax(200px,1fr))] gap-4">
        <Kpi label="Upcoming departures" value={String(kpis.departuresNext7Days)} hint={`next 7 days · ${kpis.vehiclesInUse} ${kpis.vehiclesInUse === 1 ? "vehicle" : "vehicles"}`} />
        <Kpi label="Occupancy" value={`${kpis.occupancyPercent}%`} hint={`${kpis.seatsSold} of ${kpis.seatsOnSale} seats sold, next 30 days`} bar={kpis.occupancyPercent} />
        <Kpi
          label="Revenue this month"
          value={formatMoney(kpis.revenueThisMonth)}
          hint={kpis.refundedThisMonth ? `after ${formatMoney(kpis.refundedThisMonth)} refunded` : "paid, net of refunds"}
        />
        <Kpi label="Guests today" value={String(kpis.guestsToday)} hint={`${kpis.departuresToday} departures · ${kpis.pendingHolds} ${kpis.pendingHolds === 1 ? "hold" : "holds"} pending payment`} />
      </section>

      <DepartureCalendar agencyId={agencyId} vehicles={vehicles} tours={calendarTours} slots={slots.filter((slot) => slot.localDate >= weekStart && slot.localDate <= addDays(weekStart, 6))} weekStart={weekStart} today={today} />

      <div className="grid grid-cols-[minmax(0,1fr)] gap-6 2xl:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
        <section aria-labelledby="tours-heading" className="rounded-card border border-line bg-surface p-5">
          <div className="mb-3 flex items-center justify-between">
            <h2 id="tours-heading" className="text-xl font-bold">Tours</h2>
            <Link href={`/agency/${agencyId}/tours`} className="text-sm font-semibold text-primary hover:text-primary-hover">Manage all</Link>
          </div>
          {tours.length ? (
            <div className="relative overflow-x-auto">
              <table className="w-full min-w-[560px] text-left text-sm">
                <thead className="text-xs text-muted-foreground">
                  <tr><th scope="col" className="py-2 font-semibold">Tour</th><th scope="col" className="font-semibold">Booking</th><th scope="col" className="font-semibold">Price</th><th scope="col" className="font-semibold">Next departure</th><th scope="col" className="font-semibold">Status</th></tr>
                </thead>
                <tbody className="divide-y divide-line">
                  {tours.slice(0, 6).map((tour) => {
                    const next = slots.find((slot) => slot.tourId === tour.id && slot.status === "Open" && slot.localDate >= today);
                    const nextLabel = next ? formatInZone(next.startsAt, tour.location.timeZoneId) : undefined;
                    return (
                      <tr key={tour.id}>
                        <td className="py-2.5 pr-3">
                          <Link href={`/agency/${agencyId}/tours/${tour.id}`} className="font-semibold text-ink hover:text-primary">{tour.title}</Link>
                          <span className="block text-xs text-muted-foreground">{formatDuration(tour.durationMinutes)} · up to {tour.maxGuests}</span>
                        </td>
                        <td className="pr-3">{bookingModeLabel(tour.bookingMode)}</td>
                        <td className="pr-3">{[tour.pricePerPerson && `${formatMoney(tour.pricePerPerson)} pp`, tour.privatePrice && `${formatMoney(tour.privatePrice)} trip`].filter(Boolean).join(" · ") || "–"}</td>
                        <td className="pr-3">{nextLabel ? `${nextLabel.date}, ${nextLabel.time}` : <span className="text-muted-foreground">No departures</span>}</td>
                        <td><TourStatusPill status={tour.status} /></td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">No tours yet. <Link href={`/agency/${agencyId}/tours/new`} className="font-semibold text-primary underline underline-offset-2">Create your first tour</Link>.</p>
          )}
        </section>

        <section aria-labelledby="vehicles-heading" className="rounded-card border border-line bg-surface p-5">
          <div className="mb-3 flex items-center justify-between">
            <h2 id="vehicles-heading" className="text-xl font-bold">Vehicles</h2>
            <Link href={`/agency/${agencyId}/vehicles`} className="text-sm font-semibold text-primary hover:text-primary-hover">Manage</Link>
          </div>
          <ul className="flex flex-col divide-y divide-line">
            {vehicles.map((vehicle) => {
              const count = slots.filter((slot) => slot.vehicleId === vehicle.id && slot.status === "Open" && slot.localDate >= weekStart && slot.localDate <= addDays(weekStart, 6)).length;
              return (
                <li key={vehicle.id} className="py-2.5">
                  <p className="font-semibold">{vehicle.name}</p>
                  <p className="text-[13px] text-muted-foreground">{vehicle.type} · {vehicle.capacity} seats · {count} {count === 1 ? "departure" : "departures"} this week</p>
                </li>
              );
            })}
            {!vehicles.length && <li className="py-2.5 text-sm text-muted-foreground">No vehicles yet.</li>}
          </ul>
        </section>
      </div>
    </div>
  );
}

function Kpi({ label, value, hint, bar }: { label: string; value: string; hint: string; bar?: number }) {
  return (
    <div className="rounded-card border border-line bg-surface p-5">
      <p className="text-sm text-muted-foreground">{label}</p>
      <p className="mt-1 font-display text-[32px] leading-none font-bold">{value}</p>
      {bar !== undefined && (
        <span className="mt-3 block h-1.5 overflow-hidden rounded-full bg-surface-2" aria-hidden>
          <span className="block h-full bg-primary" style={{ width: `${Math.min(bar, 100)}%` }} />
        </span>
      )}
      <p className="mt-2 text-[13px] text-muted-foreground">{hint}</p>
    </div>
  );
}
