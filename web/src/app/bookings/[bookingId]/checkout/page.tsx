import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";

import { CheckoutView, type CheckoutTour } from "@/components/bookings/checkout-view";
import type { Booking } from "@/lib/api/contracts";
import { unwrap } from "@/lib/api/client";
import { ApiError } from "@/lib/api/problem";
import { serverApi } from "@/lib/api/server";
import { requireUser } from "@/lib/auth/dal";
import { readAccessToken } from "@/lib/auth/session";
import { getTour } from "@/lib/catalog";
import { tourImage } from "@/lib/demo-media";

export const metadata: Metadata = { title: "Checkout", robots: { index: false } };
export const instant = false;

type Props = { params: Promise<{ bookingId: string }> };

export default async function CheckoutPage({ params }: Props) {
  const { bookingId } = await params;
  const user = await requireUser(`/bookings/${bookingId}/checkout`);
  const token = await readAccessToken();

  let booking: Booking;
  try {
    booking = await unwrap<Booking>(serverApi(token).GET("/api/v1/bookings/{bookingId}", { params: { path: { bookingId } } }));
  } catch (error) {
    if (error instanceof ApiError && (error.status === 404 || error.status === 400)) notFound();
    throw error;
  }

  // Only for the summary; a tour that was unpublished since doesn't block paying for the booking.
  const tour = await getTour(booking.tourId).catch(() => null);
  const cover = tour && (tour.photos.find((photo) => photo.isCover) ?? tour.photos[0]);
  const summary: CheckoutTour | null = tour && {
    city: tour.location.city,
    address: tour.location.address,
    agencyName: tour.agencyName,
    durationMinutes: tour.durationMinutes,
    image: tourImage(tour.title, cover?.urls.medium),
  };

  return (
    <div className="mx-auto max-w-[1120px] px-4 pt-8 pb-20 sm:px-6">
      <Link href="/bookings" className="text-sm font-semibold text-primary hover:text-primary-hover">← My bookings</Link>
      <div className="mt-5">
        <CheckoutView initial={booking} tour={summary} traveler={{ name: user.displayName, email: user.email }} />
      </div>
    </div>
  );
}
