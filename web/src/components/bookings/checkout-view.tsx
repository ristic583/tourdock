"use client";

import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, Check, Clock3, TimerOff } from "lucide-react";
import Link from "next/link";
import { useEffect, useId, useState } from "react";

import { useBookDeparture } from "@/components/bookings/use-book-departure";
import { FormAlert } from "@/components/feedback/form-alert";
import { Button } from "@/components/ui/button";
import { StatusPill } from "@/components/ui/status-pill";
import { bookingsApi } from "@/lib/api/bookings";
import type { Booking } from "@/lib/api/contracts";
import { ApiError } from "@/lib/api/problem";
import { freeCancellationHours, policySegments, refundPercentAt, segmentLabel } from "@/lib/availability";
import { checkoutRefetchInterval, checkoutReturnUrl, PAYMENT_POLL_TRIES, startPayment } from "@/lib/booking-attempt";
import { formatInZone, formatMoney } from "@/lib/format";
import { bookingReference } from "@/lib/my-bookings";
import { formatRemaining, useNowSeconds } from "@/lib/use-now";
import { cn } from "@/lib/utils";

const HOUR = 60 * 60 * 1000;

export type CheckoutTour = { city: string; address: string; agencyName: string; durationMinutes: number; image?: string };

type Props = { initial: Booking; tour: CheckoutTour | null; traveler: { name: string; email: string } };

function seatsLabel(booking: Booking): string {
  return booking.type === "Private"
    ? `Private trip · ${booking.guests} ${booking.guests === 1 ? "guest" : "guests"}`
    : `Shared · ${booking.guests} ${booking.guests === 1 ? "seat" : "seats"}`;
}

/**
 * The hold/checkout page: a countdown while the seats are held and the sandbox checkout, opened in this tab. The payment
 * page sends the traveler back here; the booking is then re-read for a few seconds until the provider's webhook has
 * settled the payment (Confirmed, or declined with a reason). An expired hold is re-read until the API releases it.
 */
export function CheckoutView({ initial, tour, traveler }: Props) {
  // Reads since the page loaded; the payment is only polled for a few of them.
  const [reads, setReads] = useState(0);
  const booking = useQuery({
    queryKey: ["booking", initial.id],
    queryFn: async () => {
      const next = await bookingsApi.get(initial.id);
      setReads((count) => count + 1);
      return next;
    },
    initialData: initial,
    refetchInterval: (query) => (query.state.data ? checkoutRefetchInterval(query.state.data, query.state.dataUpdateCount, Date.now()) : false),
  });
  const current = booking.data;
  const now = useNowSeconds();
  const remaining = current.holdExpiresAt && now !== null ? Math.floor(new Date(current.holdExpiresAt).getTime() / 1000) - now : null;
  const timeUp = remaining !== null && remaining <= 0;
  const { refetch } = booking;
  // The countdown reached zero: ask the API once; the refetch interval keeps asking until the hold is released.
  useEffect(() => {
    if (timeUp) void refetch();
  }, [timeUp, refetch]);
  // Coming back from the payment page via the browser's Back button can restore this page from the back/forward cache.
  useEffect(() => {
    const restored = (event: PageTransitionEvent) => {
      if (event.persisted) void refetch();
    };
    window.addEventListener("pageshow", restored);
    return () => window.removeEventListener("pageshow", restored);
  }, [refetch]);
  const checking = current.status === "Pending" && (current.lastPaymentStatus === "Pending" || current.lastPaymentStatus === "Succeeded") && (reads < PAYMENT_POLL_TRIES || booking.isFetching);

  return (
    <div className="flex flex-wrap items-start gap-8">
      <div className="min-w-0 flex-[999_1_520px]">
        {current.status === "Pending" && (
          <PendingCheckout booking={current} now={now} remaining={remaining} traveler={traveler} checking={checking} onCheckAgain={() => void refetch()} />
        )}
        {current.status === "Confirmed" || current.status === "Completed" ? <Confirmed booking={current} tour={tour} traveler={traveler} /> : null}
        {current.status === "Expired" || current.status === "Cancelled" ? <Expired booking={current} /> : null}
      </div>
      <Summary booking={current} tour={tour} />
    </div>
  );
}

type PendingProps = {
  booking: Booking;
  remaining: number | null;
  /** Seconds since the epoch, from the page's clock (null before it runs in the browser). */
  now: number | null;
  traveler: Props["traveler"];
  /** Re-reading the booking while a payment waits for the provider's answer. */
  checking: boolean;
  onCheckAgain: () => void;
};

function PendingCheckout({ booking, now, remaining, traveler, checking, onCheckAgain }: PendingProps) {
  const policyId = useId();
  const paidBefore = booking.lastPaymentStatus !== null;
  // Accepted on the way to the payment page already; coming back after a decline shouldn't ask again.
  const [accepted, setAccepted] = useState(paidBefore);
  const [busy, setBusy] = useState(false);
  const [retrying, setRetrying] = useState(false);
  const [error, setError] = useState<string>();
  const low = remaining !== null && remaining < 120;
  const timeUp = remaining !== null && remaining <= 0;

  // Back from the payment page via the back/forward cache: the button must not stay on "Opening checkout…".
  useEffect(() => {
    const restored = (event: PageTransitionEvent) => {
      if (event.persisted) setBusy(false);
    };
    window.addEventListener("pageshow", restored);
    return () => window.removeEventListener("pageshow", restored);
  }, []);

  async function pay() {
    setBusy(true);
    setError(undefined);
    try {
      const url = await startPayment(booking.id, bookingsApi, {
        returnUrl: checkoutReturnUrl(window.location.origin, booking.id),
        sleep: (ms) => {
          setRetrying(true);
          return new Promise((resolve) => setTimeout(resolve, ms));
        },
      });
      // Same tab: the payment page sends the traveler back here. Stay busy until the browser has left.
      window.location.assign(url);
    } catch (caught) {
      setBusy(false);
      setRetrying(false);
      setError(
        caught instanceof ApiError && caught.status === 503
          ? "The payment provider is still unavailable. Your seats stay held; please try again in a moment."
          : caught instanceof ApiError ? caught.userMessage : "We couldn't open the checkout. Please try again.",
      );
    }
  }

  const declined = booking.lastPaymentStatus === "Failed";
  const paidAwaitingConfirmation = booking.lastPaymentStatus === "Succeeded";
  const waiting = booking.lastPaymentStatus === "Pending" || paidAwaitingConfirmation;

  const policy = policySegments(booking.cancellationPolicy).map((segment) => `${segment.refundPercent}% ${segmentLabel(segment)}`).join(", ");
  // If the departure is already inside a low tier, say so before paying, not after.
  const refundNow = now === null ? undefined : refundPercentAt(booking.cancellationPolicy, (new Date(booking.startsAt).getTime() / 1000 - now) / 3600);

  return (
    <div className="flex flex-col gap-6">
      <div
        role="timer"
        aria-live="off"
        className={cn("flex flex-wrap items-center gap-4 rounded-card px-5 py-4", low ? "bg-urgent-soft" : "bg-primary-soft")}
      >
        <Clock3 className={cn("size-7 flex-none", low ? "text-urgent-ink" : "text-primary")} aria-hidden />
        <div className="min-w-[220px] flex-1">
          <p className="text-base font-bold">
            {timeUp ? (
              "Time's up. Checking your hold…"
            ) : (
              <>
                Your {booking.guests === 1 ? "seat is" : `${booking.guests} seats are`} held for{" "}
                <span className="font-display tabular-nums">{remaining === null ? "15:00" : formatRemaining(remaining)}</span>
              </>
            )}
          </p>
          <p className="text-sm text-muted-foreground">Finish payment before the timer runs out, or the seats go back on sale.</p>
        </div>
        <StatusPill tone="pending">Pending payment</StatusPill>
      </div>

      <h1 className="text-[34px] font-bold">Confirm and pay</h1>

      <section aria-labelledby="traveler-heading" className="flex flex-col gap-2 rounded-card border border-line bg-surface p-6">
        <h2 id="traveler-heading" className="text-[19px] font-bold">Lead traveler</h2>
        <p><span className="font-semibold">{traveler.name}</span> <span className="text-muted-foreground">· {traveler.email}</span></p>
        <p className="text-sm text-muted-foreground">The agency sees this name with your booking. The confirmation goes to this email.</p>
      </section>

      <section aria-labelledby="payment-heading" className="flex flex-col gap-4 rounded-card border border-line bg-surface p-6">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 id="payment-heading" className="text-[19px] font-bold">Payment</h2>
          <StatusPill tone="expired">Sandbox · no real charge</StatusPill>
        </div>
        <p className="text-sm text-muted-foreground">
          You pay on the payment provider&apos;s page and come back here afterwards. Retrying is safe: one booking is never
          charged twice.
        </p>
        {declined && (
          <div role="alert" className="rounded-control bg-urgent-soft px-4 py-3 text-sm text-urgent-ink">
            <span className="font-semibold">Payment declined{booking.lastPaymentFailureReason ? `: ${booking.lastPaymentFailureReason}` : ""}.</span>{" "}
            Your seats are still held. Try again.
          </div>
        )}
        {waiting && (
          <div role="status" className="flex flex-wrap items-center gap-3 rounded-control bg-surface-2 px-4 py-3 text-sm">
            <span className={cn("size-2 rounded-full bg-primary", checking && "animate-pulse")} aria-hidden />
            <span className="flex-1">
              {paidAwaitingConfirmation
                ? checking ? "Confirming your booking…" : "Payment received. Your booking confirmation is still updating. Check again in a moment."
                : checking ? "Checking your payment…" : "We haven't heard back from the payment provider yet. If you didn't finish paying, continue below."}
            </span>
            {!checking && (
              <button type="button" onClick={onCheckAgain} className="font-semibold text-primary hover:text-primary-hover">
                Check again
              </button>
            )}
          </div>
        )}
      </section>

      {refundNow !== undefined && refundNow < 100 && (
        <div role="note" className="flex items-start gap-2.5 rounded-control bg-urgent-soft px-4 py-3 text-sm text-urgent-ink">
          <AlertTriangle className="mt-0.5 size-4 flex-none" aria-hidden />
          <span>
            {refundNow === 0 ? (
              <><span className="font-semibold">Non-refundable.</span> This departure is too close for a refund under the agency&apos;s policy: if you cancel, you get nothing back.</>
            ) : (
              <><span className="font-semibold">Partly refundable.</span> This departure is already inside the {refundNow}% tier: if you cancel, you get {refundNow}% back.</>
            )}
          </span>
        </div>
      )}
      <label htmlFor={policyId} className="flex items-start gap-2.5 text-sm">
        <input id={policyId} type="checkbox" checked={accepted} onChange={(event) => setAccepted(event.target.checked)} className="mt-0.5 size-[18px] accent-[var(--primary)]" />
        <span>I accept the agency&apos;s cancellation policy: {policy}.</span>
      </label>
      <Button type="button" className="min-h-14 rounded-[14px] text-[17px]" pending={busy} disabled={!accepted || timeUp || checking || paidAwaitingConfirmation} onClick={pay}>
        {busy
          ? retrying ? "Payment provider is busy, retrying…" : "Opening checkout…"
          : paidAwaitingConfirmation ? "Payment received"
          : declined ? `Try again: pay ${formatMoney(booking.totalPrice)}`
          : waiting ? `Continue to payment (${formatMoney(booking.totalPrice)})`
          : `Pay ${formatMoney(booking.totalPrice)} and confirm`}
      </Button>
      <FormAlert message={error} />
    </div>
  );
}

function Confirmed({ booking, tour, traveler }: { booking: Booking; tour: CheckoutTour | null; traveler: Props["traveler"] }) {
  const when = formatInZone(booking.startsAt, booking.timeZoneId);
  const freeHours = freeCancellationHours(booking.cancellationPolicy);
  // The API's refund for cancelling right now tells whether the full-refund window is still open (no clock needed).
  const fullRefundNow = booking.refundIfCancelledNow.amount >= booking.paidAmount.amount && booking.paidAmount.amount > 0;
  const freeUntil = freeHours === undefined ? undefined : formatInZone(new Date(new Date(booking.startsAt).getTime() - freeHours * HOUR), booking.timeZoneId);
  return (
    <div className="flex flex-col items-start gap-5 rounded-card border border-line bg-surface p-6 sm:p-10">
      {/* React 19 hoists this into <head>: the tab says "confirmed", not "Checkout". */}
      <title>{`Booking confirmed · ${booking.tourTitle} · TourDock`}</title>
      <span className="grid size-16 place-items-center rounded-full bg-primary text-primary-foreground" aria-hidden><Check className="size-8" /></span>
      <div>
        <StatusPill tone="confirmed">Confirmed</StatusPill>
        <h1 className="mt-3 text-[30px] leading-tight font-bold sm:text-[36px]">You&apos;re going on {booking.tourTitle}</h1>
        <p className="mt-1.5 text-base text-muted-foreground">
          Confirmation for {traveler.email}. Booking reference <strong className="whitespace-nowrap text-ink">{bookingReference(booking.id)}</strong>.
        </p>
      </div>
      <div className="grid w-full grid-cols-[repeat(auto-fit,minmax(160px,1fr))] gap-3">
        <Tile label="When" value={`${when.date}, ${when.time}`} hint={`Local time (${booking.timeZoneId})`} />
        {tour && <Tile label="Meet at" value={tour.address} hint={tour.city} />}
        <Tile
          label="Free cancellation"
          value={freeUntil && fullRefundNow ? `until ${freeUntil.date}, ${freeUntil.time}` : freeUntil ? "No longer available" : "Not offered"}
          hint={fullRefundNow && freeUntil ? "full refund" : `${formatMoney(booking.refundIfCancelledNow)} back if cancelled now`}
        />
      </div>
      <Button asChild><Link href="/bookings">View my bookings</Link></Button>
    </div>
  );
}

function Tile({ label, value, hint }: { label: string; value: string; hint: string }) {
  return (
    <div className="rounded-[14px] bg-surface-2 px-4 py-3.5">
      <p className="text-[13px] text-muted-foreground">{label}</p>
      <p className="font-semibold">{value}</p>
      <p className="text-xs text-muted-foreground">{hint}</p>
    </div>
  );
}

function Expired({ booking }: { booking: Booking }) {
  const { book, submitting, error } = useBookDeparture();
  const cancelled = booking.status === "Cancelled";
  return (
    <div className="flex flex-col items-start gap-4 rounded-card border border-line bg-surface p-6 sm:p-10">
      <span className="grid size-16 place-items-center rounded-full bg-urgent-soft text-urgent-ink" aria-hidden><TimerOff className="size-8" /></span>
      <StatusPill tone={cancelled ? "cancelled" : "expired"}>{cancelled ? "Cancelled" : "Expired"}</StatusPill>
      <h1 className="text-[30px] font-bold sm:text-[34px]">{cancelled ? "This booking was cancelled" : "Your hold ran out"}</h1>
      <p className="max-w-[520px] text-base text-muted-foreground">
        {cancelled ? "Nothing more to pay here." : `The ${booking.guests === 1 ? "seat went" : `${booking.guests} seats went`} back on sale and you were not charged.`}
      </p>
      <div className="flex flex-wrap gap-2.5">
        {!cancelled && (
          <Button type="button" pending={submitting} onClick={() => book({ departureId: booking.departureId, type: booking.type, guests: booking.guests })}>
            {submitting ? "Holding your seats…" : "Hold the seats again"}
          </Button>
        )}
        <Button asChild variant="outline"><Link href={`/tours/${booking.tourId}?guests=${booking.guests}`}>Pick another time</Link></Button>
      </div>
      <FormAlert message={error} />
    </div>
  );
}

function Summary({ booking, tour }: { booking: Booking; tour: CheckoutTour | null }) {
  const start = formatInZone(booking.startsAt, booking.timeZoneId);
  const end = tour ? formatInZone(new Date(new Date(booking.startsAt).getTime() + tour.durationMinutes * 60_000), booking.timeZoneId) : undefined;
  const year = new Intl.DateTimeFormat("en-GB", { year: "numeric", timeZone: booking.timeZoneId }).format(new Date(booking.startsAt));
  return (
    <aside aria-label="Booking summary" className="w-full flex-[1_1_320px] overflow-hidden rounded-card border border-line bg-surface lg:max-w-[400px]">
      <div className={cn("relative h-[140px]", !tour?.image && "photo-sunset")}>
        {/* eslint-disable-next-line @next/next/no-img-element */}
        {tour?.image && <img src={tour.image} alt="" className="absolute inset-0 size-full object-cover" />}
      </div>
      <div className="flex flex-col gap-3.5 px-[22px] py-5">
        <div>
          {tour && <p className="text-[13px] text-muted-foreground">{tour.agencyName} · {tour.city}</p>}
          <h2 className="text-xl font-bold">{booking.tourTitle}</h2>
        </div>
        <dl className="flex flex-col gap-2 text-sm">
          <Row label="Date" value={`${start.date} ${year}`} />
          <Row label="Departure" value={end ? `${start.time} · back ~${end.time}` : start.time} />
          <Row label="Booking" value={seatsLabel(booking)} />
        </dl>
        <div className="flex flex-col gap-2 border-t border-line pt-3.5 text-sm">
          <div className="flex justify-between gap-3">
            <span className="text-muted-foreground">{booking.type === "Private" ? "Private trip" : `${booking.guests} × ${formatMoney(booking.unitPrice)} per person`}</span>
            <span>{formatMoney(booking.totalPrice)}</span>
          </div>
          <div className="mt-1 flex items-baseline justify-between">
            <span className="text-base font-bold">Total</span>
            <span className="font-display text-2xl font-bold">{formatMoney(booking.totalPrice)}</span>
          </div>
        </div>
        <p className="text-xs text-muted-foreground">Price locked when the hold started. Times are local to the tour ({booking.timeZoneId}).</p>
      </div>
    </aside>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-3">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="text-right font-medium">{value}</dd>
    </div>
  );
}
