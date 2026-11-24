"use client";

import { useRouter } from "next/navigation";
import { useId, useState } from "react";

import { Section } from "@/components/agency/tour-editor";
import { FormAlert } from "@/components/feedback/form-alert";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { DatePicker } from "@/components/ui/date-picker";
import { Input } from "@/components/ui/input";
import { agencyApi } from "@/lib/api/agency-client";
import type { AgencyTour, Vehicle } from "@/lib/api/contracts";
import { ApiError } from "@/lib/api/problem";
import { countRuleDepartures, describeRule, formatDays, WEEKDAYS, type Weekday } from "@/lib/agency-stats";
import { addDays } from "@/lib/format";
import { cn } from "@/lib/utils";

/** Departures are generated this far ahead when a rule is saved (and kept topped up by a background job). */
const HORIZON_DAYS = 365;

type Props = { agencyId: string; tour: AgencyTour; vehicles: Vehicle[]; today: string };

/**
 * Recurring departures: a vehicle, weekdays and a local start time between two dates. Saving creates the next 365 days
 * of departures at once; dates where the vehicle is already busy are skipped, so it never runs two tours at once.
 */
export function ScheduleRulesSection({ agencyId, tour, vehicles, today }: Props) {
  const router = useRouter();
  const id = useId();
  const [vehicleId, setVehicleId] = useState(vehicles[0]?.id ?? "");
  const [days, setDays] = useState<Weekday[]>(["Monday", "Wednesday", "Saturday"]);
  const [startTime, setStartTime] = useState("09:00");
  const [validFrom, setValidFrom] = useState(today);
  const [validUntil, setValidUntil] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [result, setResult] = useState<string>();
  const vehicleName = (vehicleIdToFind: string) => vehicles.find((vehicle) => vehicle.id === vehicleIdToFind)?.name ?? "Vehicle";

  const horizonEnd = addDays(today, HORIZON_DAYS);
  const previewEnd = validUntil && validUntil < horizonEnd ? validUntil : horizonEnd;
  const previewStart = validFrom > today ? validFrom : today;
  const departures = countRuleDepartures(days, previewStart, previewEnd);
  // Same rule as the API (Slot.Create): a departure sells the smaller of the vehicle's seats and the tour's group limit.
  const vehicleSeats = vehicles.find((vehicle) => vehicle.id === vehicleId)?.capacity ?? 0;
  const capacity = Math.min(vehicleSeats, tour.maxGuests);

  function toggle(day: Weekday) {
    setDays(days.includes(day) ? days.filter((item) => item !== day) : [...days, day]);
  }

  async function run(action: () => Promise<string | undefined>) {
    setBusy(true);
    setError(undefined);
    setResult(undefined);
    try {
      setResult(await action());
      router.refresh();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.userMessage : "We couldn't save the schedule.");
    } finally {
      setBusy(false);
    }
  }

  const save = (event: React.FormEvent) => {
    event.preventDefault();
    const problem = !days.length ? "Pick at least one weekday."
      : !/^\d{2}:\d{2}$/.test(startTime) ? "Enter a departure time."
      : !validFrom ? "Pick the first date of the schedule."
      : validUntil && validUntil < validFrom ? "The end date must be on or after the start date."
      : undefined;
    if (problem) {
      setResult(undefined);
      setError(problem);
      return;
    }
    run(async () => {
      const created = await agencyApi.addScheduleRule(agencyId, tour.id, { vehicleId, days: formatDays(days), startTime: `${startTime}:00`, validFrom, validUntil: validUntil || null });
      const skipped = created.departuresSkippedForConflicts;
      return `Schedule saved: ${created.departuresCreated} departures created${skipped ? `, ${skipped} skipped because ${vehicleName(vehicleId)} was already busy` : ""}.`;
    });
  };

  return (
    <Section id="schedule-heading" title="Schedule" description={`Recurring departures generate slots automatically. Times are local to the tour (${tour.location.timeZoneId}).`}>
      {tour.scheduleRules.length ? (
        <ul className="divide-y divide-line rounded-control border border-line">
          {tour.scheduleRules.map((rule) => (
            <li key={rule.id} className="flex flex-wrap items-center gap-3 px-4 py-3 text-sm">
              <span className="min-w-0 flex-1">
                <span className="block font-semibold">{describeRule(rule)}</span>
                <span className="text-muted-foreground">{vehicleName(rule.vehicleId)}{rule.isActive ? "" : " · stopped"}</span>
              </span>
              {rule.isActive && (
                <ConfirmDialog
                  tone="danger"
                  trigger={<Button variant="ghost" size="sm" disabled={busy}>Stop<span className="sr-only"> {describeRule(rule)}</span></Button>}
                  title="Stop this schedule?"
                  description={`No new departures are generated for ${describeRule(rule)}. Departures already created, and their bookings, are kept.`}
                  confirmLabel="Stop schedule"
                  pendingLabel="Stopping…"
                  onConfirm={() => agencyApi.stopScheduleRule(agencyId, tour.id, rule.id)}
                  onDone={() => {
                    setError(undefined);
                    setResult("Schedule stopped. Existing departures and bookings are kept.");
                    router.refresh();
                  }}
                />
              )}
            </li>
          ))}
        </ul>
      ) : (
        <p className="text-sm text-muted-foreground">No schedule yet. Add one below, or add one-off departures in the calendar.</p>
      )}

      {vehicles.length === 0 ? (
        <p className="rounded-control bg-surface-2 px-4 py-3 text-sm text-muted-foreground">Add a vehicle first: schedules run on a vehicle.</p>
      ) : (
        <form noValidate onSubmit={save} className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_280px]">
          <div className="flex flex-col gap-4">
            <label className="flex flex-col gap-1.5 text-sm font-semibold" htmlFor={`${id}-vehicle`}>
              Vehicle
              <select id={`${id}-vehicle`} className="h-11 rounded-control border border-line bg-surface px-3 font-normal" value={vehicleId} onChange={(event) => setVehicleId(event.target.value)}>
                {vehicles.map((vehicle) => <option key={vehicle.id} value={vehicle.id}>{vehicle.name} ({vehicle.type.toLowerCase()}, {vehicle.capacity} seats)</option>)}
              </select>
            </label>
            <fieldset>
              <legend className="mb-1.5 text-sm font-semibold">Repeats every</legend>
              <div className="flex flex-wrap gap-1.5">
                {WEEKDAYS.map((day) => (
                  <button key={day} type="button" aria-pressed={days.includes(day)} onClick={() => toggle(day)} className={cn("min-h-10 min-w-12 cursor-pointer rounded-control border px-3 text-sm font-semibold", days.includes(day) ? "border-ink bg-ink text-bg" : "border-line bg-surface hover:border-muted-foreground")}>
                    {day.slice(0, 3)}
                  </button>
                ))}
              </div>
            </fieldset>
            <div className="grid gap-3 sm:grid-cols-3">
              <label className="flex flex-col gap-1.5 text-sm font-semibold" htmlFor={`${id}-time`}>Departs at<Input id={`${id}-time`} type="time" value={startTime} onChange={(event) => setStartTime(event.target.value)} /></label>
              <div className="flex flex-col gap-1.5 text-sm font-semibold"><label htmlFor={`${id}-from`}>Valid from</label><DatePicker id={`${id}-from`} label="Valid from" min={today} value={validFrom} onChange={setValidFrom} /></div>
              <div className="flex flex-col gap-1.5 text-sm font-semibold"><label htmlFor={`${id}-until`}>Valid until (optional)</label><DatePicker id={`${id}-until`} label="Valid until" placeholder="No end date" clearable min={validFrom || today} value={validUntil} onChange={setValidUntil} /></div>
            </div>
          </div>
          <div className="flex flex-col gap-3 rounded-card bg-surface-2 p-4" aria-live="polite">
            <p className="label-caps">Preview</p>
            <p className="text-sm">Every <strong>{days.map((day) => day.slice(0, 3)).join(", ") || "(pick days)"}</strong> at <strong>{startTime}</strong> on <strong>{vehicleName(vehicleId)}</strong></p>
            <div className="grid grid-cols-2 gap-2">
              <div><p className="font-display text-2xl font-bold">{departures}</p><p className="text-xs text-muted-foreground">departures, next {HORIZON_DAYS} days</p></div>
              <div><p className="font-display text-2xl font-bold">{departures * capacity}</p><p className="text-xs text-muted-foreground">seats on sale</p></div>
            </div>
            {capacity < vehicleSeats && (
              <p className="text-xs text-muted-foreground">{capacity} seats per departure: the tour&apos;s guest limit is lower than the vehicle&apos;s {vehicleSeats} seats.</p>
            )}
            <p className="text-xs text-muted-foreground">Dates when {vehicleName(vehicleId)} is already busy are skipped. Seasonal prices apply per departure.</p>
            <Button type="submit" pending={busy} disabled={!days.length || !vehicleId}>{busy ? "Saving…" : "Save schedule"}</Button>
          </div>
        </form>
      )}
      <FormAlert message={error} />
      {result && <p role="status" className="text-sm font-medium text-primary">{result}</p>}
    </Section>
  );
}
