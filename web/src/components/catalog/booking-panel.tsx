"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { ChevronLeft, ChevronRight, Minus, Plus } from "lucide-react";
import Link from "next/link";
import { useEffect, useId, useState } from "react";

import { useBookDeparture } from "@/components/bookings/use-book-departure";
import { ApproxPrice, RatesNote } from "@/components/catalog/approx-price";
import { useDepartureSelection } from "@/components/catalog/departure-selection";
import { FormAlert } from "@/components/feedback/form-alert";
import { Button } from "@/components/ui/button";
import type { BookingMode, BookingType, Departure, Money } from "@/lib/api/contracts";
import { browserApi } from "@/lib/api/browser";
import { unwrap } from "@/lib/api/client";
import { seats } from "@/lib/availability";
import type { DisplayRate } from "@/lib/currency";
import { addDays, formatInZone, formatLocal, formatMoney } from "@/lib/format";
import { cn } from "@/lib/utils";

/** How far ahead the API publishes departures. */
const HORIZON_DAYS = 365;

export type BookingPanelProps = {
  tourId: string;
  bookingMode: BookingMode;
  maxGuests: number;
  pricePerPerson: Money | null;
  privatePrice: Money | null;
  timeZone: string;
  today: string;
  initialGuests: number;
  initialWeek: string;
  /** Departures for `initialWeek` and `initialGuests`, when the server already has them. */
  initialDepartures?: Departure[];
  signedIn: boolean;
  /** Approximate prices in the traveler's display currency; null shows EUR only. */
  rate?: DisplayRate | null;
};

function bookable(departure: Departure): boolean {
  return departure.canBookSeats || departure.canBookPrivately;
}

function weekLabel(start: string): string {
  const end = addDays(start, 6);
  const format = (date: string, options: Intl.DateTimeFormatOptions) => new Intl.DateTimeFormat("en-GB", { ...options, timeZone: "UTC" }).format(new Date(`${date}T00:00:00Z`));
  return start.slice(0, 7) === end.slice(0, 7)
    ? format(start, { month: "long", year: "numeric" })
    : `${format(start, { month: "short" })} – ${format(end, { month: "short", year: "numeric" })}`;
}

/**
 * The sticky booking side panel: a week of days, the departures of the picked day, Shared/Private (following each
 * departure's canBookSeats / canBookPrivately for this party size), guests and the total. Departures are fetched
 * per week and party size through the BFF; the first week comes from the server.
 */
export function BookingPanel(props: BookingPanelProps) {
  const { tourId, bookingMode, maxGuests, timeZone, today, signedIn } = props;
  const guestsId = useId();
  const [guests, setGuests] = useState(props.initialGuests);
  const [week, setWeek] = useState(props.initialWeek);
  const [pickedDay, setPickedDay] = useState<string>();
  const [pickedId, setPickedId] = useState<string>();
  const [pickedType, setPickedType] = useState<BookingType>(bookingMode === "Private" ? "Private" : "Shared");
  const { select } = useDepartureSelection();
  const { book, error, submitting } = useBookDeparture();

  const departures = useQuery({
    queryKey: ["departures", tourId, guests, week],
    queryFn: () =>
      unwrap<Departure[]>(
        browserApi.GET("/api/v1/tours/{tourId}/departures", { params: { path: { tourId }, query: { guests, from: week, to: addDays(week, 6) } } }),
      ),
    initialData: guests === props.initialGuests && week === props.initialWeek ? props.initialDepartures : undefined,
    placeholderData: keepPreviousData,
  });

  // Days of the shown week, each with its departures. Only days with something bookable can be picked.
  // While another week or party size loads, the previous answer stays on screen (but can't be booked: its
  // canBookSeats / canBookPrivately were computed for other guests).
  const list = departures.data ?? [];
  const stale = departures.isPlaceholderData;
  const days = Array.from({ length: 7 }, (_, index) => {
    const date = addDays(week, index);
    const onDay = list.filter((departure) => departure.localStart.startsWith(date));
    const open = onDay.filter(bookable);
    const prices = open.map((departure) => departure.pricePerPerson?.amount ?? departure.privatePrice?.amount).filter((n): n is number => n !== undefined);
    const currency = open.find((departure) => departure.pricePerPerson ?? departure.privatePrice);
    const cheapest = prices.length && currency ? formatMoney({ amount: Math.min(...prices), currency: (currency.pricePerPerson ?? currency.privatePrice)!.currency }) : undefined;
    return { date, departures: onDay, open, note: open.length ? cheapest ?? "Open" : onDay.length ? "Full" : "–" };
  });

  // The picked day and departure, or the first open ones when the pick no longer applies (other week or party size).
  const day = days.find((d) => d.date === pickedDay && d.open.length) ?? days.find((d) => d.open.length);
  const departure = day?.open.find((d) => d.id === pickedId) ?? day?.open[0];
  const type: BookingType | undefined = departure
    ? pickedType === "Private" ? (departure.canBookPrivately ? "Private" : "Shared") : departure.canBookSeats ? "Shared" : "Private"
    : undefined;

  // Share the pick with the cancellation policy, and keep guests and date in the URL so the page can be shared.
  useEffect(() => select(departure), [departure, select]);
  const dayDate = day?.date;
  useEffect(() => {
    const url = new URL(window.location.href);
    if (guests > 1) url.searchParams.set("guests", String(guests));
    else url.searchParams.delete("guests");
    if (dayDate) url.searchParams.set("date", dayDate);
    window.history.replaceState(window.history.state, "", url);
  }, [guests, dayDate]);

  const allowsShared = bookingMode !== "Private";
  const allowsPrivate = bookingMode !== "Shared";
  const unit = type === "Private" ? departure?.privatePrice : departure?.pricePerPerson;
  const total = unit && (type === "Private" ? unit : { amount: Math.round(unit.amount * guests * 100) / 100, currency: unit.currency });
  const seatsLeft = departure ? seats(departure.remainingSeats, departure.capacity) : undefined;
  const guestLimit = Math.min(maxGuests, 100);
  const locked = submitting;
  const returnTo = `/tours/${tourId}?${new URLSearchParams({ ...(guests > 1 ? { guests: String(guests) } : {}), ...(day ? { date: day.date } : {}) })}`;

  return (
    <div className="flex flex-col gap-[18px] rounded-hero border border-line bg-surface p-[22px] shadow-card">
      <div className="flex flex-wrap items-baseline gap-1.5">
        {props.pricePerPerson ? (
          <>
            <span className="text-sm text-muted-foreground">from</span>
            <span className="font-display text-[30px] font-bold">{formatMoney(props.pricePerPerson)}</span>
            <span className="text-muted-foreground">/ person</span>
            <ApproxPrice money={props.pricePerPerson} rate={props.rate} className="basis-full" />
          </>
        ) : (
          <>
            <span className="font-display text-[30px] font-bold">{formatMoney(props.privatePrice)}</span>
            <span className="text-muted-foreground">/ private trip</span>
            <ApproxPrice money={props.privatePrice} rate={props.rate} className="basis-full" />
          </>
        )}
        <span className="flex-1" />
        {props.pricePerPerson && props.privatePrice && <span className="text-[13px] text-muted-foreground">or {formatMoney(props.privatePrice)} / private trip</span>}
      </div>

      <div>
        <div className="mb-2 flex items-center justify-between">
          <p className="font-semibold" aria-live="polite">{weekLabel(week)}</p>
          <div className="flex gap-1">
            <StepButton label="Previous week" disabled={week <= today || locked} onClick={() => setWeek(week <= addDays(today, 7) ? today : addDays(week, -7))}><ChevronLeft /></StepButton>
            <StepButton label="Next week" disabled={addDays(week, 7) > addDays(today, HORIZON_DAYS) || locked} onClick={() => setWeek(addDays(week, 7))}><ChevronRight /></StepButton>
          </div>
        </div>
        <div className={cn("grid grid-cols-7 gap-1 transition-opacity", stale && "opacity-60")} aria-busy={departures.isFetching}>
          {days.map((d) => {
            const label = formatLocal(`${d.date}T00:00:00`);
            const on = d.date === day?.date;
            return (
              <button
                key={d.date}
                type="button"
                disabled={!d.open.length || locked}
                aria-pressed={on}
                // Starts with the visible text (weekday, day, note), then says it in full (WCAG 2.5.3).
                aria-label={`${label.weekday} ${label.day}${d.note ? ` ${d.note}` : ""}: ${label.date}, ${d.open.length ? `${d.open.length} departure${d.open.length === 1 ? "" : "s"}` : d.note === "Full" ? "full" : "no departures"}`}
                onClick={() => { setPickedDay(d.date); setPickedId(undefined); }}
                className={cn(
                  "flex min-w-0 cursor-pointer flex-col items-center gap-px rounded-control border px-0.5 py-2 text-xs transition-colors disabled:cursor-not-allowed disabled:bg-surface-2 disabled:opacity-55",
                  on ? "border-primary bg-primary-soft shadow-[inset_0_0_0_1px_var(--primary)]" : "border-line bg-surface hover:border-muted-foreground",
                )}
              >
                <span className="text-[11px] text-muted-foreground">{label.weekday}</span>{" "}
                <span className="text-[15px] font-bold">{label.day}</span>{" "}
                <span className="max-w-full truncate text-[10px] text-muted-foreground">{d.note}</span>
              </button>
            );
          })}
        </div>
      </div>

      <div className="flex flex-col gap-2">
        <p className="font-semibold">Departures</p>
        {departures.isError ? (
          <p className="text-sm text-muted-foreground">We couldn&apos;t load departures. <button type="button" className="font-semibold text-primary" onClick={() => departures.refetch()}>Try again</button></p>
        ) : !day ? (
          <p className="rounded-control bg-surface-2 px-3.5 py-3 text-sm text-muted-foreground">
            {departures.isPending ? "Loading departures…" : `No departures for ${guests} ${guests === 1 ? "guest" : "guests"} this week. Try the next week or fewer guests.`}
          </p>
        ) : (
          day.departures.map((slot) => {
            const start = formatLocal(slot.localStart);
            const end = formatInZone(slot.endsAt, timeZone);
            const seat = seats(slot.remainingSeats, slot.capacity);
            const on = slot.id === departure?.id;
            return (
              <button
                key={slot.id}
                type="button"
                disabled={!bookable(slot) || locked}
                aria-pressed={on}
                onClick={() => setPickedId(slot.id)}
                className={cn(
                  "flex min-h-14 w-full cursor-pointer items-center gap-3 rounded-[14px] border px-3.5 py-3 text-left disabled:cursor-not-allowed disabled:opacity-55",
                  on ? "border-primary shadow-[inset_0_0_0_1px_var(--primary)]" : "border-line bg-surface hover:border-muted-foreground",
                )}
              >
                <span className="w-14 font-display text-lg font-bold">{start.time}</span>
                <span className="min-w-0 flex-1 text-xs text-muted-foreground">
                  Back ~{end.time}
                  {slot.canBookPrivately && slot.remainingSeats === slot.capacity ? " · private available" : ""}
                </span>
                <span className="text-right">
                  <span className={cn("block text-[13px] font-semibold", seat.urgent && "text-urgent-ink")}>{seat.label}</span>
                  <span className="mt-1 block h-1 w-16 overflow-hidden rounded-full bg-surface-2" aria-hidden>
                    <span className="block h-full bg-primary" style={{ width: `${seat.filledPercent}%` }} />
                  </span>
                </span>
              </button>
            );
          })
        )}
      </div>

      {allowsShared && allowsPrivate && (
        <div className="grid grid-cols-2 gap-1 rounded-[14px] bg-surface-2 p-1" role="group" aria-label="Booking type">
          {(["Shared", "Private"] as const).map((option) => {
            const enabled = option === "Shared" ? departure?.canBookSeats : departure?.canBookPrivately;
            const price = option === "Shared" ? departure?.pricePerPerson ?? props.pricePerPerson : departure?.privatePrice ?? props.privatePrice;
            return (
              <button
                key={option}
                type="button"
                disabled={!enabled || locked}
                aria-pressed={type === option}
                onClick={() => setPickedType(option)}
                className={cn("min-h-11 cursor-pointer rounded-[10px] text-sm leading-tight font-semibold disabled:cursor-not-allowed disabled:text-muted-foreground", type === option && "bg-surface shadow-card")}
              >
                {option === "Shared" ? "Seats" : "Private trip"}
                <br />
                <span className="text-xs font-medium text-muted-foreground">{formatMoney(price)} {option === "Shared" ? "per person" : "whole trip"}</span>
              </button>
            );
          })}
        </div>
      )}
      {allowsPrivate && departure && !departure.canBookPrivately && (
        <p className="-mt-2 rounded-[10px] bg-urgent-soft px-3 py-2.5 text-[13px] text-urgent-ink">
          {departure.remainingSeats < departure.capacity
            ? "Private booking is only possible while no seats on this departure are sold. Try another departure."
            : `A private trip on this departure takes up to ${departure.capacity} guests.`}
        </p>
      )}

      <div className="flex items-center justify-between gap-3">
        <div>
          <label htmlFor={guestsId} className="font-semibold">Guests</label>
          <p className="text-[13px] text-muted-foreground">
            {type === "Private" && departure ? `Up to ${departure.capacity} on a private trip` : seatsLeft ? `${seatsLeft.left} seats left on this departure` : `Up to ${maxGuests} per booking`}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <StepButton label="Fewer guests" disabled={guests <= 1 || locked} onClick={() => setGuests(guests - 1)}><Minus /></StepButton>
          <input
            id={guestsId}
            type="number"
            inputMode="numeric"
            min={1}
            max={guestLimit}
            value={guests}
            disabled={locked}
            onChange={(event) => {
              const value = Number(event.target.value);
              if (Number.isInteger(value) && value >= 1) setGuests(Math.min(value, guestLimit));
            }}
            className="h-10 w-12 rounded-control border border-line bg-surface text-center text-lg font-bold [appearance:textfield] [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none"
          />
          <StepButton label="More guests" disabled={guests >= guestLimit || locked} onClick={() => setGuests(guests + 1)}><Plus /></StepButton>
        </div>
      </div>

      <div className="flex flex-col gap-2 border-t border-line pt-4 text-sm">
        {unit && (
          <div className="flex justify-between gap-3">
            <span className="text-muted-foreground">{type === "Private" ? `Private trip (up to ${departure?.capacity} guests)` : `${guests} × ${formatMoney(unit)} per person`}</span>
            <span>{formatMoney(total ?? null)}</span>
          </div>
        )}
        <div className="mt-1 flex items-baseline justify-between">
          <span className="text-base font-bold">Total</span>
          <span className="font-display text-2xl font-bold">{total ? formatMoney(total) : "–"}</span>
        </div>
        {total && <ApproxPrice money={total} rate={props.rate} className="self-end" />}
      </div>

      {signedIn ? (
        <Button
          type="button"
          className="min-h-[52px] rounded-[14px] text-base"
          pending={submitting}
          disabled={!departure || !type || stale}
          onClick={() => departure && type && book({ departureId: departure.id, type, guests })}
        >
          {submitting ? "Holding your seats…" : "Reserve · hold for 15 min"}
        </Button>
      ) : (
        <Button asChild className="min-h-[52px] rounded-[14px] text-base">
          <Link href={`/login?next=${encodeURIComponent(returnTo)}`}>Sign in to book</Link>
        </Button>
      )}
      <FormAlert message={error} />
      <RatesNote rate={props.rate} className="-mt-1.5 text-center" />
      <p className="-mt-1.5 text-center text-xs text-muted-foreground">You won&apos;t be charged yet. Seats are held for 15 minutes while you pay. Times are local ({timeZone}).</p>
    </div>
  );
}

function StepButton({ label, disabled, onClick, children }: { label: string; disabled?: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      type="button"
      aria-label={label}
      disabled={disabled}
      onClick={onClick}
      className="grid size-10 cursor-pointer place-items-center rounded-full border border-line bg-surface hover:border-muted-foreground disabled:cursor-not-allowed disabled:opacity-40 [&_svg]:size-4"
    >
      {children}
    </button>
  );
}
