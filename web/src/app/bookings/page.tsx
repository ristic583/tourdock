import type { Metadata } from "next";
import Link from "next/link";

import { BookingToastProvider } from "@/components/bookings/booking-toast";
import { HoldCountdown } from "@/components/bookings/hold-notice";
import { ManageBookingDialog } from "@/components/bookings/manage-booking-dialog";
import { ReviewDialog } from "@/components/reviews/review-dialog";
import { Stars } from "@/components/reviews/stars";
import { Button } from "@/components/ui/button";
import { StatusPill, type StatusTone } from "@/components/ui/status-pill";
import type { Booking, BookingStatus, MyReview } from "@/lib/api/contracts";
import { unwrap } from "@/lib/api/client";
import { serverApi } from "@/lib/api/server";
import { requireUser } from "@/lib/auth/dal";
import { readAccessToken } from "@/lib/auth/session";
import { getTour } from "@/lib/catalog";
import { tourImage } from "@/lib/demo-media";
import { formatInZone, formatMoney } from "@/lib/format";
import { bookingNote, bookingReference, groupBookings, parseTab, paymentText, seatsText, TABS, type BookingTab } from "@/lib/my-bookings";
import { canStillReview } from "@/lib/reviews";
import { cn } from "@/lib/utils";

export const metadata: Metadata = { title: "My bookings", robots: { index: false } };
// Private, per-session data: rendered on demand.
export const instant = false;

const STATUS: Record<BookingStatus, { tone: StatusTone; label: string }> = {
  Pending: { tone: "pending", label: "Pending payment" },
  Confirmed: { tone: "confirmed", label: "Confirmed" },
  Completed: { tone: "completed", label: "Completed" },
  Cancelled: { tone: "cancelled", label: "Cancelled" },
  Expired: { tone: "expired", label: "Expired" },
};

const PLACEHOLDERS = ["photo-sunset", "photo-sea", "photo-island", "photo-hills", "photo-deep", "photo-desert"];

const EMPTY: Record<BookingTab, string> = {
  upcoming: "No upcoming trips. Find a tour and pick a departure to get started.",
  past: "Trips you've taken will show up here.",
  cancelled: "Nothing cancelled or expired.",
};

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

/** The request's clock, read once outside rendering so tabs and notes agree. */
function requestTime(): number {
  return Date.now();
}

export default async function BookingsPage({ searchParams }: Props) {
  await requireUser("/bookings");
  const tab = parseTab((await searchParams).tab);
  const token = await readAccessToken();
  const [bookings, reviews] = await Promise.all([
    unwrap<Booking[]>(serverApi(token).GET("/api/v1/me/bookings")),
    // Only needed for the Past tab's review buttons; without it the buttons are simply left out.
    unwrap<MyReview[]>(serverApi(token).GET("/api/v1/me/reviews")).catch(() => undefined),
  ]);
  const reviewByBooking = new Map((reviews ?? []).map((review) => [review.bookingId, review]));
  const now = requestTime();
  const groups = groupBookings(bookings, now);
  const shown = groups[tab];

  // Cover photos for the list (one request per tour, cached per render; a missing tour just gets a placeholder).
  const tourIds = [...new Set(shown.map((booking) => booking.tourId))];
  const covers = new Map(
    await Promise.all(
      tourIds.map(async (id) => {
        const tour = await getTour(id).catch(() => null);
        const photo = tour && (tour.photos.find((p) => p.isCover) ?? tour.photos[0]);
        return [id, tour ? tourImage(tour.title, photo?.urls.thumbnail) : undefined] as const;
      }),
    ),
  );

  return (
    <div className="mx-auto max-w-[1120px] px-4 pt-9 pb-20 sm:px-6">
      <h1 className="text-4xl font-bold">My bookings</h1>
      {/* Scrolls sideways on narrow screens. The baseline is an inset shadow, not a border, so the active tab's underline
          can sit on it without poking out of the scroll box (which showed a stray vertical scrollbar). */}
      <nav
        aria-label="Booking lists"
        className="mt-5 mb-6 flex gap-1 overflow-x-auto overflow-y-hidden shadow-[inset_0_-1px_0_var(--color-line)] [scrollbar-width:none] [&::-webkit-scrollbar]:hidden"
      >
        {TABS.map((item) => {
          const on = item.id === tab;
          return (
            <Link
              key={item.id}
              href={item.id === "upcoming" ? "/bookings" : `/bookings?tab=${item.id}`}
              aria-current={on ? "page" : undefined}
              className={cn(
                "inline-flex min-h-11 items-center gap-1.5 border-b-2 px-3.5 font-semibold whitespace-nowrap",
                on ? "border-primary text-ink" : "border-transparent text-muted-foreground hover:text-ink",
              )}
            >
              {item.label} <span className="font-medium text-muted-foreground">{groups[item.id].length}</span>
            </Link>
          );
        })}
      </nav>

      <BookingToastProvider>
        {shown.length ? (
          <ul className="flex flex-col gap-4">
            {shown.map((booking, index) => (
              <BookingRow
                key={booking.id}
                booking={booking}
                tab={tab}
                now={now}
                image={covers.get(booking.tourId)}
                placeholder={PLACEHOLDERS[index % PLACEHOLDERS.length]}
                review={reviews ? (reviewByBooking.get(booking.id) ?? null) : undefined}
              />
            ))}
          </ul>
        ) : (
          <div className="rounded-card border border-dashed border-line bg-surface p-8 text-muted-foreground">
            <p>{EMPTY[tab]}</p>
            {tab === "upcoming" && <Button asChild className="mt-4"><Link href="/tours">Find a tour</Link></Button>}
          </div>
        )}
      </BookingToastProvider>
    </div>
  );
}

/** `review`: the traveler's review of this booking, null for none, undefined when reviews couldn't be loaded. */
function BookingRow({ booking, tab, now, image, placeholder, review }: { booking: Booking; tab: BookingTab; now: number; image?: string; placeholder: string; review?: MyReview | null }) {
  const status = STATUS[booking.status];
  const when = formatInZone(booking.startsAt, booking.timeZoneId);
  const reference = bookingReference(booking.id);
  const note = bookingNote(booking, now);
  const canManage = booking.status === "Confirmed" && tab === "upcoming";

  return (
    <li id={`booking-${booking.id}`} className="scroll-mt-24 overflow-hidden rounded-card border border-line bg-surface">
      <article className="flex flex-wrap items-center gap-5 p-4">
        <div className={cn("relative h-[104px] w-[148px] flex-none overflow-hidden rounded-[14px]", !image && placeholder)}>
          {/* eslint-disable-next-line @next/next/no-img-element */}
          {image && <img src={image} alt="" loading="lazy" className="absolute inset-0 size-full object-cover" />}
        </div>
        <div className="flex min-w-0 flex-[1_1_260px] flex-col gap-1">
          <div className="flex flex-wrap items-center gap-2.5">
            <StatusPill tone={status.tone}>{status.label}</StatusPill>
            <span className="text-[13px] text-muted-foreground">{reference}</span>
          </div>
          <h2 className="text-[19px] font-bold">
            <Link href={`/tours/${booking.tourId}`} className="text-ink hover:text-primary">{booking.tourTitle}</Link>
          </h2>
          <p className="text-sm text-muted-foreground">
            {when.date}, {when.time} <span className="whitespace-nowrap">({booking.timeZoneId})</span> · {seatsText(booking)}
          </p>
          {booking.status === "Pending" && booking.holdExpiresAt && <HoldCountdown expiresAt={booking.holdExpiresAt} />}
          {review && (
            <p className="flex items-center gap-2 text-[13px] text-muted-foreground">
              <Stars rating={review.rating} /> You reviewed this trip{review.isHidden ? " (hidden by our moderators)" : ""}
            </p>
          )}
          {note && (
            <p className={cn("text-[13px] font-medium", note.tone === "primary" ? "text-primary" : note.tone === "urgent" ? "text-urgent-ink" : "text-muted-foreground")}>
              {note.text}
            </p>
          )}
        </div>
        <div className="min-w-[90px] text-right">
          <p className="font-display text-xl font-bold">{formatMoney(booking.totalPrice)}</p>
          <p className="text-xs text-muted-foreground">{paymentText(booking)}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          {canManage && <ManageBookingDialog booking={booking} reference={reference} />}
          {booking.status === "Pending" && (
            <Button asChild><Link href={`/bookings/${booking.id}/checkout`}>Finish payment</Link></Button>
          )}
          {booking.status === "Completed" && review !== undefined && (review || canStillReview(booking.reviewableUntil, now)) && (
            <ReviewDialog bookingId={booking.id} tourTitle={booking.tourTitle} review={review ?? undefined} canEdit={review ? new Date(review.editableUntil).getTime() > now : true} />
          )}
          {tab !== "upcoming" && (
            <Button asChild variant="outline"><Link href={`/tours/${booking.tourId}?guests=${booking.guests}`}>Book again</Link></Button>
          )}
        </div>
      </article>
    </li>
  );
}
