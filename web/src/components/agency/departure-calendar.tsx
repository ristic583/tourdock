"use client";

import { ChevronLeft, ChevronRight } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useId, useState } from "react";

import { FormAlert } from "@/components/feedback/form-alert";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "@/components/ui/dialog";
import { StatusPill } from "@/components/ui/status-pill";
import { agencyApi } from "@/lib/api/agency-client";
import type { Slot, Vehicle } from "@/lib/api/contracts";
import { ApiError } from "@/lib/api/problem";
import { slotKind } from "@/lib/agency-stats";
import { addDays, formatInZone, formatLocal } from "@/lib/format";
import { cn } from "@/lib/utils";

export type CalendarTour = { id: string; title: string; timeZoneId: string };

type Props = {
  agencyId: string;
  vehicles: Vehicle[];
  tours: CalendarTour[];
  slots: Slot[];
  weekStart: string;
  today: string;
  /** Base URL for week navigation (`?week=` is appended); omit to hide the arrows. */
  navigateTo?: string;
};

const CHIP: Record<ReturnType<typeof slotKind>, string> = {
  shared: "border-primary/30 bg-primary-soft text-ink",
  private: "border-[var(--status-completed-ink)]/30 bg-[var(--status-completed-bg)] text-ink",
  full: "border-line bg-surface-2 text-ink",
  blocked: "border-dashed border-line bg-surface text-muted-foreground",
  cancelled: "border-line bg-surface text-muted-foreground line-through",
};

const LEGEND: { kind: keyof typeof CHIP; label: string }[] = [
  { kind: "shared", label: "Shared" },
  { kind: "private", label: "Private" },
  { kind: "full", label: "Full" },
  { kind: "blocked", label: "Blocked" },
];

/** Departures by vehicle for one week. Each departure opens a dialog to block, unblock or cancel it. */
export function DepartureCalendar({ agencyId, vehicles, tours, slots, weekStart, today, navigateTo }: Props) {
  const [selected, setSelected] = useState<Slot>();
  const tourById = new Map(tours.map((tour) => [tour.id, tour]));
  const days = Array.from({ length: 7 }, (_, index) => addDays(weekStart, index));
  const end = formatLocal(`${days[6]}T00:00:00`);
  const start = formatLocal(`${weekStart}T00:00:00`);

  return (
    <section aria-labelledby="calendar-heading" className="rounded-card border border-line bg-surface p-5">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-3">
          <h2 id="calendar-heading" className="text-xl font-bold">Departures by vehicle</h2>
          {navigateTo && (
            <div className="flex items-center gap-1">
              <Button asChild variant="outline" size="icon" aria-label="Previous week"><Link href={`${navigateTo}?week=${addDays(weekStart, -7)}`}><ChevronLeft /></Link></Button>
              <span className="px-2 text-sm font-semibold">{start.day} {start.date.split(" ").at(-1)} – {end.day} {end.date.split(" ").at(-1)}</span>
              <Button asChild variant="outline" size="icon" aria-label="Next week"><Link href={`${navigateTo}?week=${addDays(weekStart, 7)}`}><ChevronRight /></Link></Button>
            </div>
          )}
        </div>
        <ul className="flex flex-wrap gap-3 text-[13px] text-muted-foreground" aria-label="Legend">
          {LEGEND.map((item) => (
            <li key={item.kind} className="flex items-center gap-1.5"><span className={cn("size-3 rounded-sm border", CHIP[item.kind])} aria-hidden />{item.label}</li>
          ))}
        </ul>
      </div>

      {vehicles.length === 0 ? (
        <p className="rounded-control bg-surface-2 px-4 py-3 text-sm text-muted-foreground">Add a vehicle first: departures run on a vehicle.</p>
      ) : (
        <div className="relative overflow-x-auto">
          <table className="w-full min-w-[860px] table-fixed border-separate border-spacing-1 text-left">
            <caption className="sr-only">Week of {start.date}, departures per vehicle</caption>
            <thead>
              <tr>
                <th scope="col" className="w-36 px-2 text-xs font-semibold text-muted-foreground">Vehicle</th>
                {days.map((day) => {
                  const label = formatLocal(`${day}T00:00:00`);
                  return (
                    <th key={day} scope="col" className={cn("px-2 text-xs font-semibold", day === today ? "text-primary" : "text-muted-foreground")}>
                      {label.weekday} {label.day}{day === today && <span className="sr-only"> (today)</span>}
                    </th>
                  );
                })}
              </tr>
            </thead>
            <tbody>
              {vehicles.map((vehicle) => (
                <tr key={vehicle.id}>
                  <th scope="row" className="align-top px-2 py-1.5 font-normal">
                    <p className="truncate text-sm font-semibold">{vehicle.name}</p>
                    <p className="text-xs text-muted-foreground">{vehicle.type} · {vehicle.capacity} seats</p>
                  </th>
                  {days.map((day) => {
                    const cell = slots.filter((slot) => slot.vehicleId === vehicle.id && slot.localDate === day).sort((a, b) => a.startsAt.localeCompare(b.startsAt));
                    return (
                      <td key={day} className={cn("align-top rounded-control p-1", day === today && "bg-primary-soft/40")}>
                        <div className="flex flex-col gap-1">
                          {cell.map((slot) => {
                            const tour = tourById.get(slot.tourId);
                            const kind = slotKind(slot);
                            const time = formatInZone(slot.startsAt, tour?.timeZoneId ?? "UTC").time;
                            const sold = slot.isPrivatelyBooked ? slot.capacity : slot.reservedSeats;
                            const occupancy = kind === "blocked" ? "Blocked" : kind === "cancelled" ? "Cancelled" : kind === "private" ? "Private" : kind === "full" ? "Full" : `${sold}/${slot.capacity}`;
                            return (
                              <button
                                key={slot.id}
                                type="button"
                                onClick={() => setSelected(slot)}
                                // Starts with the visible text in reading order (WCAG 2.5.3), then the occupancy in words.
                                aria-label={`${time} ${occupancy} ${tour?.title ?? "Tour"}, ${occupancy === `${sold}/${slot.capacity}` ? `${sold} of ${slot.capacity} seats sold` : occupancy}`}
                                className={cn("w-full cursor-pointer rounded-[10px] border px-2 py-1.5 text-left text-xs hover:brightness-95", CHIP[kind])}
                              >
                                <span className="flex justify-between gap-1 font-semibold"><span>{time}</span>{" "}<span>{occupancy}</span></span>{" "}
                                <span className="line-clamp-2 break-words" title={tour?.title}>{tour?.title ?? "Tour"}</span>
                                {kind === "shared" && (
                                  <span className="mt-1 block h-1 overflow-hidden rounded-full bg-surface" aria-hidden>
                                    <span className="block h-full bg-primary" style={{ width: `${Math.round((sold / Math.max(slot.capacity, 1)) * 100)}%` }} />
                                  </span>
                                )}
                              </button>
                            );
                          })}
                        </div>
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {selected && (
        <SlotDialog
          agencyId={agencyId}
          slot={selected}
          tour={tourById.get(selected.tourId)}
          vehicle={vehicles.find((vehicle) => vehicle.id === selected.vehicleId)}
          onClose={() => setSelected(undefined)}
        />
      )}
    </section>
  );
}

function SlotDialog({ agencyId, slot, tour, vehicle, onClose }: { agencyId: string; slot: Slot; tour?: CalendarTour; vehicle?: Vehicle; onClose: () => void }) {
  const router = useRouter();
  const reasonId = useId();
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [confirmCancel, setConfirmCancel] = useState(false);
  const when = formatInZone(slot.startsAt, tour?.timeZoneId ?? "UTC");
  const sold = slot.isPrivatelyBooked ? slot.capacity : slot.reservedSeats;

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setError(undefined);
    try {
      await action();
      router.refresh();
      onClose();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.userMessage : "That didn't work. Please try again.");
      setBusy(false);
    }
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <div>
          <DialogTitle>{tour?.title ?? "Departure"}</DialogTitle>
          <DialogDescription className="mt-1">
            {when.date}, {when.time} ({tour?.timeZoneId ?? "UTC"}) · {vehicle?.name ?? "Vehicle"}
          </DialogDescription>
        </div>
        <div className="flex flex-wrap items-center gap-3 text-sm">
          <StatusPill tone={slot.status === "Open" ? "confirmed" : slot.status === "Blocked" ? "pending" : "cancelled"}>{slot.status}</StatusPill>
          <span className="text-muted-foreground">
            {slot.isPrivatelyBooked ? "Booked privately" : `${sold} of ${slot.capacity} seats sold`}{slot.scheduleRuleId ? " · from a schedule rule" : " · one-off"}
          </span>
        </div>

        {slot.status === "Open" && (
          <p className="text-sm text-muted-foreground">
            Blocking stops new bookings (only possible while nothing is sold). Cancelling cancels every booking on it with a full refund.
          </p>
        )}

        {confirmCancel && (
          <label htmlFor={reasonId} className="flex flex-col gap-1.5 text-sm font-semibold">
            Reason (shown to travelers)
            <textarea id={reasonId} rows={2} maxLength={500} value={reason} onChange={(event) => setReason(event.target.value)} className="rounded-control border border-line bg-surface px-3 py-2 font-normal" />
          </label>
        )}

        <FormAlert message={error} />
        <div className="flex flex-wrap gap-2">
          {slot.status === "Open" && !confirmCancel && (
            <ConfirmDialog
              trigger={<Button variant="outline" disabled={busy || sold > 0}>Block</Button>}
              title="Block this departure?"
              description="It stays in your calendar but travelers can't book it. You can unblock it later."
              confirmLabel="Block departure"
              pendingLabel="Blocking…"
              onConfirm={() => agencyApi.blockSlot(agencyId, slot.id)}
              onDone={() => { onClose(); router.refresh(); }}
            />
          )}
          {slot.status === "Blocked" && (
            <Button variant="outline" pending={busy} onClick={() => run(() => agencyApi.unblockSlot(agencyId, slot.id))}>{busy ? "Unblocking…" : "Unblock"}</Button>
          )}
          {slot.status !== "Cancelled" &&
            (confirmCancel ? (
              <Button variant="urgent" pending={busy} disabled={reason.trim().length < 3} onClick={() => run(() => agencyApi.cancelSlot(agencyId, slot.id, reason.trim()))}>
                {busy ? "Cancelling…" : sold > 0 ? "Cancel and refund everyone" : "Cancel departure"}
              </Button>
            ) : (
              <Button variant="urgent" disabled={busy} onClick={() => setConfirmCancel(true)}>Cancel departure…</Button>
            ))}
        </div>
      </DialogContent>
    </Dialog>
  );
}
