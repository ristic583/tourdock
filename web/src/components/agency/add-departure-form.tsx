"use client";

import { useRouter } from "next/navigation";
import { useId, useState } from "react";

import { FormAlert } from "@/components/feedback/form-alert";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { Input } from "@/components/ui/input";
import { agencyApi } from "@/lib/api/agency-client";
import type { Vehicle } from "@/lib/api/contracts";
import { ApiError } from "@/lib/api/problem";

const selectClass = "h-11 w-full rounded-control border border-line bg-surface px-3";

/** One-off departure: tour, vehicle and a start time in the tour's local time. */
export function AddDepartureForm({ agencyId, tours, vehicles, defaultDate }: { agencyId: string; tours: { id: string; title: string; timeZoneId: string }[]; vehicles: Vehicle[]; defaultDate: string }) {
  const router = useRouter();
  const id = useId();
  const [tourId, setTourId] = useState(tours[0]?.id ?? "");
  const [vehicleId, setVehicleId] = useState(vehicles[0]?.id ?? "");
  const [date, setDate] = useState(defaultDate);
  const [time, setTime] = useState("09:00");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string>();
  const [error, setError] = useState<string>();
  const tour = tours.find((item) => item.id === tourId);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    const problem = !date ? "Pick the departure date." : !/^\d{2}:\d{2}$/.test(time) ? "Enter the departure time." : undefined;
    if (problem) {
      setMessage(undefined);
      setError(problem);
      return;
    }
    setBusy(true);
    setError(undefined);
    setMessage(undefined);
    try {
      await agencyApi.addSlot(agencyId, tourId, { vehicleId, localStart: `${date}T${time}:00` });
      setMessage("Departure added.");
      router.refresh();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.userMessage : "We couldn't add the departure.");
    } finally {
      setBusy(false);
    }
  }

  if (!tours.length || !vehicles.length) return null;
  return (
    <form noValidate onSubmit={submit} className="flex flex-col gap-4 rounded-card border border-line bg-surface p-5" aria-labelledby={`${id}-title`}>
      <h2 id={`${id}-title`} className="text-xl font-bold">Add a one-off departure</h2>
      <div className="grid gap-3 sm:grid-cols-4">
        <label className="flex flex-col gap-1.5 text-sm font-semibold">Tour
          <select className={selectClass} value={tourId} onChange={(event) => setTourId(event.target.value)}>
            {tours.map((item) => <option key={item.id} value={item.id}>{item.title}</option>)}
          </select>
        </label>
        <label className="flex flex-col gap-1.5 text-sm font-semibold">Vehicle
          <select className={selectClass} value={vehicleId} onChange={(event) => setVehicleId(event.target.value)}>
            {vehicles.map((item) => <option key={item.id} value={item.id}>{item.name} ({item.capacity} seats)</option>)}
          </select>
        </label>
        <div className="flex flex-col gap-1.5 text-sm font-semibold">
          <label htmlFor={`${id}-date`}>Date</label>
          <DatePicker id={`${id}-date`} label="Departure date" disablePast value={date} onChange={setDate} />
        </div>
        <label className="flex flex-col gap-1.5 text-sm font-semibold" htmlFor={`${id}-time`}>Starts ({tour?.timeZoneId ?? "local time"})
          <Input id={`${id}-time`} type="time" value={time} onChange={(event) => setTime(event.target.value)} />
        </label>
      </div>
      <FormAlert message={error} />
      {message && <p role="status" className="text-sm font-medium text-primary">{message}</p>}
      <Button type="submit" className="self-start" pending={busy}>{busy ? "Adding…" : "Add departure"}</Button>
    </form>
  );
}
