"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Minus, Plus } from "lucide-react";
import { useRouter } from "next/navigation";
import { useId, useState } from "react";

import { useBookingToast } from "@/components/bookings/booking-toast";
import { FormAlert } from "@/components/feedback/form-alert";
import { Button } from "@/components/ui/button";
import { Dialog, DialogClose, DialogContent, DialogDescription, DialogTitle, DialogTrigger } from "@/components/ui/dialog";
import { bookingsApi } from "@/lib/api/bookings";
import type { Booking } from "@/lib/api/contracts";
import { ApiError } from "@/lib/api/problem";
import { policySegments, segmentLabel } from "@/lib/availability";
import { formatMoney } from "@/lib/format";
import { currentSegment, refundPreview } from "@/lib/my-bookings";
import { cn } from "@/lib/utils";

/**
 * "Change or cancel" for a confirmed booking: keep fewer seats (reduce guests) or none (cancel), with a refund
 * preview from the API's `refundIfCancelledNow`, re-read when the dialog opens so it reflects this moment.
 */
export function ManageBookingDialog({ booking: listed, reference }: { booking: Booking; reference: string }) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const notify = useBookingToast();
  const reasonId = useId();
  const [open, setOpen] = useState(false);
  const [openedAt, setOpenedAt] = useState(0);
  const canReduce = listed.type === "Shared" && listed.guests > 1;
  const [keep, setKeep] = useState(canReduce ? listed.guests - 1 : 0);
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();

  const fresh = useQuery({
    queryKey: ["booking", listed.id],
    queryFn: () => bookingsApi.get(listed.id),
    initialData: listed,
    staleTime: 0,
    enabled: open,
  });
  const booking = fresh.data;
  const preview = refundPreview(booking, keep);
  const segments = policySegments(booking.cancellationPolicy);
  const current = openedAt ? currentSegment(booking.cancellationPolicy, booking.startsAt, openedAt) : undefined;
  const hoursLeft = openedAt ? Math.max(0, Math.floor((new Date(booking.startsAt).getTime() - openedAt) / 3_600_000)) : undefined;
  const whole = keep === 0;

  function onOpenChange(next: boolean) {
    setOpen(next);
    if (next) {
      setOpenedAt(Date.now());
      setKeep(canReduce ? listed.guests - 1 : 0);
      setReason("");
      setError(undefined);
    }
  }

  async function confirm() {
    setBusy(true);
    setError(undefined);
    try {
      const result = whole ? await bookingsApi.cancel(booking.id, reason) : await bookingsApi.reduceGuests(booking.id, keep);
      const refund = result.refund.amount > 0 ? ` A refund of ${formatMoney(result.refund)} is on its way.` : "";
      notify(whole ? `Booking ${reference} cancelled.${refund}` : `${preview.removed} ${preview.removed === 1 ? "seat" : "seats"} cancelled on ${reference}.${refund}`);
      queryClient.setQueryData(["booking", booking.id], result.booking);
      setOpen(false);
      router.refresh();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.userMessage : "We couldn't change this booking. Please try again.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogTrigger asChild>
        <Button variant="outline">Change or cancel</Button>
      </DialogTrigger>
      <DialogContent>
        <div>
          <DialogTitle>{canReduce ? "Change seats or cancel" : "Cancel booking"}</DialogTitle>
          <DialogDescription className="mt-1">
            {booking.tourTitle} · {reference}. {canReduce ? "Reduce to cancel some seats, or go to 0 to cancel the whole booking." : "The whole booking is cancelled."}
          </DialogDescription>
        </div>

        {canReduce && (
          <div className="flex items-center gap-3.5">
            <Stepper label="Fewer seats" disabled={keep <= 0 || busy} onClick={() => setKeep(keep - 1)}><Minus /></Stepper>
            <div className="min-w-[90px] text-center">
              <p className="font-display text-[30px] leading-none font-bold" aria-live="polite">{keep}</p>
              <p className="text-xs text-muted-foreground">of {booking.guests} seats kept</p>
            </div>
            <Stepper label="More seats" disabled={keep >= booking.guests - 1 || busy} onClick={() => setKeep(keep + 1)}><Plus /></Stepper>
          </div>
        )}

        <div>
          <p className="mb-2 text-[13px] font-semibold">Where you are on the policy</p>
          <ol className="grid gap-[3px] overflow-hidden rounded-lg text-xs font-semibold" style={{ gridTemplateColumns: segments.map((s) => `${s.weight}fr`).join(" ") }}>
            {segments.map((segment) => {
              const on = segment.fromHours === current?.fromHours;
              return (
                <li key={segment.fromHours} aria-current={on ? "step" : undefined} className={cn("min-w-0 px-2.5 py-2 leading-tight", on ? "bg-primary text-primary-foreground" : "bg-surface-2 text-muted-foreground")}>
                  <span className="block">{segment.refundPercent}%</span>
                  <span className="block font-medium">{segmentLabel(segment)}</span>
                </li>
              );
            })}
          </ol>
          {hoursLeft !== undefined && (
            <p className="mt-2 text-[13px] text-muted-foreground">
              Departure in {hoursLeft >= 24 ? `${Math.floor(hoursLeft / 24)} days ` : ""}{hoursLeft % 24}h, counted to the departure time at the tour ({booking.timeZoneId}).
            </p>
          )}
        </div>

        <section aria-label="Refund preview" className="flex flex-col gap-2.5 rounded-card border border-line p-5 text-sm">
          <h3 className="font-sans text-[17px] font-bold tracking-normal">Refund preview</h3>
          {!whole && <Line label="Seats cancelled" value={`${preview.removed} × ${formatMoney(booking.unitPrice)}`} />}
          <Line label={whole ? "Booking total" : "Value of cancelled seats"} value={formatMoney(preview.removedValue)} />
          <Line label="Policy tier" value={`${preview.percent}% refund`} />
          <div className="flex items-baseline justify-between border-t border-line pt-2.5">
            <span className="font-bold">You get back</span>
            <span className="font-display text-2xl font-bold text-primary">{formatMoney(preview.refund)}</span>
          </div>
          {!whole && <Line label="New booking total" value={formatMoney(preview.newTotal)} strong />}
          <p className="text-xs text-muted-foreground">Refunds go back to the original payment method. {fresh.isFetching ? "Updating…" : ""}</p>
        </section>

        {whole && (
          <label htmlFor={reasonId} className="flex flex-col gap-1.5 text-sm font-semibold">
            Reason (optional)
            <textarea
              id={reasonId}
              value={reason}
              maxLength={500}
              rows={2}
              onChange={(event) => setReason(event.target.value)}
              className="rounded-control border border-line bg-surface px-3 py-2 font-normal text-ink outline-none focus-visible:border-primary"
            />
          </label>
        )}

        <FormAlert message={error} />
        <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <DialogClose asChild><Button type="button" variant="outline" disabled={busy}>Keep booking</Button></DialogClose>
          <Button type="button" variant={whole ? "urgent" : "default"} pending={busy} disabled={preview.removed === 0} onClick={confirm}>
          {busy ? "Cancelling…" : whole ? "Cancel whole booking" : `Cancel ${preview.removed} ${preview.removed === 1 ? "seat" : "seats"}`}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function Line({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="flex justify-between gap-3">
      <span className="text-muted-foreground">{label}</span>
      <span className={cn(strong && "font-semibold")}>{value}</span>
    </div>
  );
}

function Stepper({ label, disabled, onClick, children }: { label: string; disabled: boolean; onClick: () => void; children: React.ReactNode }) {
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
