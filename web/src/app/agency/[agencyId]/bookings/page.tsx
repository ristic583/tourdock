import type { Metadata } from "next";
import Link from "next/link";

import { CancelBookingDialog } from "@/components/agency/cancel-booking-dialog";
import { DatePicker } from "@/components/ui/date-picker";
import { StatusPill, type StatusTone } from "@/components/ui/status-pill";
import { agencyTimeZone, listAgencyBookings, listAgencyTours, requireAgency } from "@/lib/agency";
import type { BookingStatus } from "@/lib/api/contracts";
import { formatPickedDate } from "@/lib/calendar-dates";
import { addDays, formatInZone, formatMoney, todayIn } from "@/lib/format";
import { bookingReference } from "@/lib/my-bookings";
import { cn } from "@/lib/utils";

// Private, per-user back-office data: rendered on demand, never prerendered.
export const instant = false;

export const metadata: Metadata = { title: "Agency bookings" };

const STATUS: Record<BookingStatus, { tone: StatusTone; label: string }> = {
  Pending: { tone: "pending", label: "Pending payment" },
  Confirmed: { tone: "confirmed", label: "Confirmed" },
  Completed: { tone: "completed", label: "Completed" },
  Cancelled: { tone: "cancelled", label: "Cancelled" },
  Expired: { tone: "expired", label: "Expired" },
};
const FILTERS: (BookingStatus | "All")[] = ["All", "Confirmed", "Pending", "Cancelled", "Completed", "Expired"];
const DATE = /^\d{4}-\d{2}-\d{2}$/;

type Props = { params: Promise<{ agencyId: string }>; searchParams: Promise<{ from?: string; to?: string; status?: string }> };

export default async function AgencyBookingsPage({ params, searchParams }: Props) {
  const { agencyId } = await params;
  await requireAgency(agencyId, `/agency/${agencyId}/bookings`);
  const query = await searchParams;
  const tours = await listAgencyTours(agencyId);
  const today = todayIn(agencyTimeZone(tours));
  const from = query.from && DATE.test(query.from) ? query.from : today;
  const to = query.to && DATE.test(query.to) && query.to >= from ? query.to : addDays(from, 30);
  const status = FILTERS.find((item) => item === query.status) ?? "All";
  const all = await listAgencyBookings(agencyId, from, to);
  const shown = (status === "All" ? all : all.filter(({ booking }) => booking.status === status)).sort((a, b) => a.booking.startsAt.localeCompare(b.booking.startsAt));
  const href = (changes: Record<string, string>) => `/agency/${agencyId}/bookings?${new URLSearchParams({ from, to, status, ...changes })}`;

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-[32px] font-bold">Bookings</h1>
        <p className="text-muted-foreground">Departures from {formatPickedDate(from)} to {formatPickedDate(to)} (local dates).</p>
      </div>
      <form noValidate className="flex flex-wrap items-end gap-3" action={`/agency/${agencyId}/bookings`}>
        <div className="flex w-[190px] flex-col gap-1 text-sm font-semibold"><span>From</span><DatePicker label="From" name="from" defaultValue={from} max={to} /></div>
        <div className="flex w-[190px] flex-col gap-1 text-sm font-semibold"><span>To</span><DatePicker label="To" name="to" defaultValue={to} min={from} /></div>
        <input type="hidden" name="status" value={status} />
        <button type="submit" className="h-11 cursor-pointer rounded-control border border-line bg-surface px-4 font-semibold hover:border-muted-foreground">Show</button>
      </form>
      <nav aria-label="Status filter" className="flex flex-wrap gap-2">
        {FILTERS.map((item) => (
          <Link
            key={item}
            href={href({ status: item })}
            aria-current={item === status ? "page" : undefined}
            className={cn("inline-flex min-h-9 items-center rounded-full border px-3.5 text-sm font-medium", item === status ? "border-ink bg-ink text-bg" : "border-line bg-surface text-ink hover:border-muted-foreground")}
          >
            {item === "Pending" ? "Pending payment" : item} <span className="ml-1.5 opacity-70">{item === "All" ? all.length : all.filter(({ booking }) => booking.status === item).length}</span>
          </Link>
        ))}
      </nav>

      {shown.length ? (
        <div className="relative overflow-x-auto rounded-card border border-line bg-surface">
          <table className="w-full min-w-[820px] text-left text-sm">
            <thead className="border-b border-line text-xs text-muted-foreground">
              <tr>
                <th scope="col" className="px-4 py-3 font-semibold">Departure</th>
                <th scope="col" className="font-semibold">Tour</th>
                <th scope="col" className="font-semibold">Traveler</th>
                <th scope="col" className="font-semibold">Booking</th>
                <th scope="col" className="font-semibold">Total</th>
                <th scope="col" className="font-semibold">Status</th>
                <th scope="col"><span className="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {shown.map(({ booking, customerName, customerEmail }) => {
                const when = formatInZone(booking.startsAt, booking.timeZoneId);
                const reference = bookingReference(booking.id);
                return (
                  <tr key={booking.id}>
                    <td className="px-4 py-3 whitespace-nowrap">{when.date}, {when.time}</td>
                    <td className="pr-3">{booking.tourTitle}</td>
                    <td className="pr-3"><span className="block font-semibold">{customerName}</span><span className="text-xs text-muted-foreground">{customerEmail}</span></td>
                    <td className="pr-3"><span className="block">{booking.type === "Private" ? "Private trip" : "Shared"} · {booking.guests} {booking.guests === 1 ? "guest" : "guests"}</span><span className="text-xs text-muted-foreground">{reference}</span></td>
                    <td className="pr-3 whitespace-nowrap">{formatMoney(booking.totalPrice)}{booking.refundedAmount.amount > 0 && <span className="block text-xs text-muted-foreground">{formatMoney(booking.refundedAmount)} refunded</span>}</td>
                    <td className="pr-3"><StatusPill tone={STATUS[booking.status].tone}>{STATUS[booking.status].label}</StatusPill></td>
                    <td className="pr-4 text-right">
                      {(booking.status === "Confirmed" || booking.status === "Pending") && <CancelBookingDialog agencyId={agencyId} bookingId={booking.id} label={`${reference}, ${customerName}, ${booking.tourTitle}`} />}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="rounded-card border border-dashed border-line bg-surface p-6 text-muted-foreground">No bookings in this range.</p>
      )}
    </div>
  );
}
