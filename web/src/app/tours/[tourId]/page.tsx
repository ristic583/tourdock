import type { Metadata } from "next";
import { CalendarCheck, Clock3, MapPin, Ship, Star, UsersRound } from "lucide-react";
import Link from "next/link";
import { notFound } from "next/navigation";
import type { ComponentType, ReactNode } from "react";

import { BookingPanel } from "@/components/catalog/booking-panel";
import { CancellationPolicy } from "@/components/catalog/cancellation-policy";
import { DepartureSelectionProvider } from "@/components/catalog/departure-selection";
import { DepartureWeatherCard } from "@/components/catalog/departure-weather";
import { TourGallery } from "@/components/catalog/tour-gallery";
import { TourReviews } from "@/components/reviews/tour-reviews";
import type { PublicTour, TourReviews as TourReviewsData } from "@/lib/api/contracts";
import { unwrap } from "@/lib/api/client";
import { serverApi } from "@/lib/api/server";
import { ApiError } from "@/lib/api/problem";
import { getCurrentUser } from "@/lib/auth/dal";
import { firstBookable, freeCancellationHours, hoursLabel } from "@/lib/availability";
import { getDepartures, getTour } from "@/lib/catalog";
import { getDisplayRate } from "@/lib/display-currency";
import { REVIEWS_PAGE_SIZE } from "@/lib/reviews";
import { meetingPointLinks } from "@/lib/meeting-point";
import { addDays, bookingModeLabel, formatDuration, todayIn } from "@/lib/format";

type Props = { params: Promise<{ tourId: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> };
export const instant = false;

function guestsFrom(value: string | string[] | undefined): number {
  const number = typeof value === "string" ? Number(value) : NaN;
  return Number.isInteger(number) && number >= 1 && number <= 100 ? number : 1;
}

function dateFrom(value: string | string[] | undefined): string | undefined {
  return typeof value === "string" && /^\d{4}-\d{2}-\d{2}$/.test(value) ? value : undefined;
}

/** `getTour` is wrapped in React `cache()`, so metadata and page share one request. */
async function loadTour(tourId: string): Promise<PublicTour> {
  try {
    return await getTour(tourId);
  } catch (error) {
    if (error instanceof ApiError && (error.status === 404 || error.status === 400)) notFound();
    throw error;
  }
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  try {
    const tour = await getTour((await params).tourId);
    return { title: tour.title, description: `${tour.description.slice(0, 150)} · ${tour.location.city}` };
  } catch {
    return { title: "Tour" };
  }
}

function Fact({ icon: Icon, label, children }: { icon: ComponentType<{ className?: string }>; label: string; children: ReactNode }) {
  return (
    <div className="flex items-start gap-3">
      <span className="grid size-10 flex-none place-items-center rounded-control bg-surface-2 text-primary" aria-hidden><Icon className="size-5" /></span>
      <div>
        <p className="text-[13px] text-muted-foreground">{label}</p>
        <p className="font-semibold">{children}</p>
      </div>
    </div>
  );
}

function initials(name: string): string {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((word) => word[0]!.toUpperCase()).join("");
}

/** The first bookable departure after `start`, within the year departures are published for (62-day API pages). */
async function firstBookableLater(tourId: string, guests: number, start: string) {
  for (let from = start; from < addDays(start, 335); from = addDays(from, 61)) {
    const page = await getDepartures(tourId, guests, from, addDays(from, 60)).catch(() => undefined);
    const found = page && firstBookable(page);
    if (found || !page) return found;
  }
  return undefined;
}

export default async function TourPage({ params, searchParams }: Props) {
  const { tourId } = await params;
  const query = await searchParams;
  const guests = guestsFrom(query.guests);
  const tour = await loadTour(tourId);
  const timeZone = tour.location.timeZoneId;
  const today = todayIn(timeZone);
  const wanted = dateFrom(query.date);
  const from = wanted && wanted > today ? wanted : today;

  // One request covers the next 30 days: it finds the first open day, and usually also holds that whole week.
  const [upcoming, user, rate, reviews] = await Promise.all([
    getDepartures(tourId, guests, from, addDays(from, 29)).catch(() => undefined),
    getCurrentUser().catch(() => null),
    getDisplayRate(),
    // Reviews are a nice-to-have on this page: if they can't load, the section is left out.
    unwrap<TourReviewsData>(serverApi().GET("/api/v1/tours/{tourId}/reviews", { params: { path: { tourId }, query: { pageSize: REVIEWS_PAGE_SIZE } } })).catch(() => undefined),
  ]);
  // Seasonal tours can be months away (a summer boat trip seen in November): look further ahead, a page at a time.
  const first = (upcoming && firstBookable(upcoming)) ?? (upcoming ? await firstBookableLater(tourId, guests, addDays(from, 30)) : undefined);
  const week = first && first.localStart.slice(0, 10) > addDays(from, 6) ? first.localStart.slice(0, 10) : from;
  const covered = upcoming && addDays(week, 6) <= addDays(from, 29);
  const initialDepartures = covered ? upcoming.filter((d) => d.localStart.slice(0, 10) >= week && d.localStart.slice(0, 10) <= addDays(week, 6)) : undefined;

  const freeUntil = freeCancellationHours(tour.cancellationPolicy);
  const shared = tour.bookingMode !== "Private";
  const privately = tour.bookingMode !== "Shared";

  return (
    <div className="mx-auto max-w-page px-4 pt-6 pb-20 sm:px-6">
      <nav aria-label="Breadcrumb" className="flex flex-wrap gap-2 text-sm text-muted-foreground">
        <Link href="/tours" className="text-primary hover:text-primary-hover">Tours</Link>
        <span aria-hidden>/</span>
        <Link href={`/tours?city=${encodeURIComponent(tour.location.city)}`} className="text-primary hover:text-primary-hover">{tour.location.city}</Link>
        <span aria-hidden>/</span>
        <span aria-current="page">{tour.title}</span>
      </nav>

      <div className="mt-3 mb-5">
        <h1 className="text-[32px] leading-[1.1] font-bold sm:text-[42px]">{tour.title}</h1>
        <div className="mt-2.5 flex flex-wrap items-center gap-4 text-[15px]">
          {tour.averageRating !== null && (
            <span className="inline-flex items-center gap-1 font-semibold">
              <Star className="size-4 fill-star text-star" aria-hidden />
              {tour.averageRating.toFixed(1)}
              <a href="#reviews-heading" className="font-medium text-muted-foreground hover:text-ink">· {tour.reviewCount} {tour.reviewCount === 1 ? "review" : "reviews"}</a>
            </span>
          )}
          <span className="text-muted-foreground">{tour.location.city}, {tour.location.countryCode}</span>
          <span className="flex gap-1.5">
            {shared && <span className="inline-flex h-6 items-center rounded-full bg-surface-2 px-2.5 text-xs font-semibold">Shared</span>}
            {privately && <span className="inline-flex h-6 items-center rounded-full bg-surface-2 px-2.5 text-xs font-semibold">Private</span>}
          </span>
        </div>
      </div>

      <TourGallery title={tour.title} photos={tour.photos} />

      <DepartureSelectionProvider>
        <div className="mt-6 grid grid-cols-[minmax(0,1fr)] gap-8 lg:mt-9 lg:flex lg:flex-wrap lg:items-start lg:gap-10">
          <div className="flex min-w-0 flex-[999_1_560px] flex-col gap-10">
            <div className="flex flex-wrap items-center gap-4 rounded-card border border-line bg-surface px-5 py-[18px]">
              <span className="grid size-12 place-items-center rounded-[14px] bg-primary-soft font-display font-bold text-primary" aria-hidden>{initials(tour.agencyName)}</span>
              <div className="min-w-[200px] flex-1">
                <p className="font-semibold">Run by {tour.agencyName}</p>
                <p className="text-sm text-muted-foreground">Local agency on TourDock · departure times in {timeZone}</p>
              </div>
            </div>

            <div className="grid grid-cols-[repeat(auto-fill,minmax(180px,1fr))] gap-[18px]">
              <Fact icon={Clock3} label="Duration">{formatDuration(tour.durationMinutes)}</Fact>
              <Fact icon={UsersRound} label="Group size">Up to {tour.maxGuests} guests</Fact>
              <Fact icon={Ship} label="Booking">{bookingModeLabel(tour.bookingMode)}</Fact>
              <Fact icon={CalendarCheck} label="Free cancellation">{freeUntil === undefined ? "Not offered" : freeUntil === 0 ? "Up to departure" : `Up to ${hoursLabel(freeUntil)} before`}</Fact>
            </div>

            <DepartureWeatherCard fallbackDepartureId={first?.id} timeZone={timeZone} />

            <section aria-labelledby="about-heading">
              <h2 id="about-heading" className="mb-3 text-2xl font-bold">About this tour</h2>
              <p className="max-w-[680px] text-base leading-[1.7] whitespace-pre-line">{tour.description}</p>
            </section>

            <section aria-labelledby="meeting-heading" className="overflow-hidden rounded-card border border-line bg-surface">
              <iframe
                title={`Meeting point map: ${tour.location.address}, ${tour.location.city}`}
                src={meetingPointLinks(tour.location.latitude, tour.location.longitude).embed}
                loading="lazy"
                referrerPolicy="no-referrer"
                className="h-[260px] w-full border-0 sm:h-[300px]"
              />
              <div className="flex flex-wrap items-center justify-between gap-3 px-[18px] py-4">
                <div>
                  <h2 id="meeting-heading" className="font-sans text-[15px] font-semibold tracking-normal">Meeting point</h2>
                  <p className="text-sm text-muted-foreground">{tour.location.address}, {tour.location.city}</p>
                </div>
                <a
                  href={meetingPointLinks(tour.location.latitude, tour.location.longitude).full}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex items-center gap-1.5 text-sm font-semibold text-primary hover:text-primary-hover"
                >
                  <MapPin className="size-4" aria-hidden /> Open map<span className="sr-only"> (opens in a new tab)</span>
                </a>
              </div>
            </section>

            <CancellationPolicy tiers={tour.cancellationPolicy} timeZone={timeZone} />

            {reviews && <TourReviews tourId={tour.id} initial={reviews} timeZone={timeZone} signedIn={Boolean(user)} />}
          </div>

          {/* On phones the booking panel comes right after the photos (the design's booking sheet); beside the content from lg. */}
          <aside aria-label="Book this tour" className="w-full flex-[1_1_360px] max-lg:order-first lg:sticky lg:top-[88px] lg:max-w-[420px]">
            <BookingPanel
              tourId={tour.id}
              bookingMode={tour.bookingMode}
              maxGuests={tour.maxGuests}
              pricePerPerson={tour.pricePerPerson}
              privatePrice={tour.privatePrice}
              timeZone={timeZone}
              today={today}
              initialGuests={guests}
              initialWeek={week}
              initialDepartures={initialDepartures}
              signedIn={Boolean(user)}
              rate={rate}
            />
          </aside>
        </div>
      </DepartureSelectionProvider>
    </div>
  );
}
