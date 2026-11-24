"use client";

import { useState } from "react";
import { useWatch, type FieldErrors, type FieldValues, type Path, type PathValue, type UseFormRegister, type UseFormReturn } from "react-hook-form";

import { MeetingPointMap } from "@/components/agency/meeting-point-map";
import { CityCombobox } from "@/components/catalog/city-combobox";

import { FormField, describedBy } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import type { DetailsValues, PricingValues } from "@/lib/tour-forms";
import { timeZones } from "@/lib/tour-forms";

const selectClass = "h-11 w-full rounded-control border border-line bg-surface px-3";

type FieldProps<T extends FieldValues> = { prefix: string; register: UseFormRegister<T>; errors: FieldErrors<T> };

function field<T extends FieldValues>(prefix: string, errors: FieldErrors<T>, name: string) {
  const id = `${prefix}-${name}`;
  const error = (errors as Record<string, { message?: string } | undefined>)[name]?.message;
  return { id, error, a11y: { "aria-invalid": Boolean(error), "aria-describedby": describedBy(id, error) } };
}

/**
 * Title, description, location and size. The place comes from the city autocomplete (which also fills the country and
 * time zone) and the pin on the map: no typing coordinates, and no silent default location.
 */
export function DetailsFields<T extends DetailsValues>({ prefix, form: anyForm }: { prefix: string; form: UseFormReturn<T> }) {
  // The create form adds pricing fields; these fields only touch the details part, so work with that shape.
  const form = anyForm as unknown as UseFormReturn<DetailsValues>;
  const errors = form.formState.errors;
  const f = (name: keyof DetailsValues) => field(prefix, errors, name);
  const r = (name: keyof DetailsValues) => form.register(name);
  const set = <K extends keyof DetailsValues>(name: K, value: DetailsValues[K], validate = true) =>
    form.setValue(name, value as PathValue<DetailsValues, K>, { shouldDirty: true, shouldValidate: validate });
  const [latitude, longitude, city] = useWatch({ control: form.control, name: ["latitude", "longitude", "city"] });
  const point = Number.isFinite(latitude) && Number.isFinite(longitude) ? { latitude, longitude } : null;
  const [focusKey, setFocusKey] = useState<string>();
  const pinError = f("latitude").error ?? f("longitude").error;

  return (
    <div className="grid gap-4 sm:grid-cols-2">
      <div className="sm:col-span-2"><FormField id={f("title").id} label="Title" error={f("title").error}><Input id={f("title").id} {...f("title").a11y} {...r("title")} /></FormField></div>
      <div className="sm:col-span-2">
        <FormField id={f("description").id} label="Description" error={f("description").error}>
          <textarea id={f("description").id} rows={5} className="rounded-control border border-line bg-surface px-3 py-2" {...f("description").a11y} {...r("description")} />
        </FormField>
      </div>
      <FormField id={f("city").id} label="City" error={f("city").error} hint="Pick it from the list to fill the country, time zone and map.">
        <CityCombobox
          id={f("city").id}
          variant="field"
          placeholder="Start typing, e.g. Split"
          defaultValue={city ?? ""}
          {...f("city").a11y}
          onTextChange={(text) => set("city", text, false)}
          onPick={(picked) => {
            if (!picked) return;
            set("city", picked.name);
            set("countryCode", picked.countryCode);
            set("timeZoneId", picked.timeZoneId);
            set("latitude", picked.latitude);
            set("longitude", picked.longitude);
            setFocusKey(picked.id);
          }}
        />
      </FormField>
      <FormField id={f("countryCode").id} label="Country code" error={f("countryCode").error}><Input id={f("countryCode").id} maxLength={2} className="uppercase" {...f("countryCode").a11y} {...r("countryCode")} /></FormField>
      <div className="sm:col-span-2"><FormField id={f("address").id} label="Meeting point" error={f("address").error} hint="What travelers look for, e.g. “Riva waterfront, pier 3”."><Input id={f("address").id} {...f("address").a11y} {...r("address")} /></FormField></div>
      <div className="flex flex-col gap-1.5 sm:col-span-2">
        <p className="text-sm font-semibold">Pin on the map</p>
        <MeetingPointMap
          label="Map: click the exact spot or drag the pin"
          value={point}
          focusKey={focusKey}
          onChange={(next) => {
            set("latitude", next.latitude);
            set("longitude", next.longitude);
          }}
        />
        {pinError ? (
          <p role="alert" className="text-[13px] text-urgent-ink">{pinError}</p>
        ) : (
          <p className="text-[13px] text-muted-foreground">
            {point ? `${point.latitude.toFixed(5)}, ${point.longitude.toFixed(5)} · drag the pin or click the map to move it.` : "Pick the city first, then click the exact meeting point."}
          </p>
        )}
      </div>
      <div className="sm:col-span-2">
        <FormField id={f("timeZoneId").id} label="Time zone" error={f("timeZoneId").error}>
          <select id={f("timeZoneId").id} className={selectClass} {...f("timeZoneId").a11y} {...r("timeZoneId")}>
            {timeZones().map((zone) => <option key={zone} value={zone}>{zone}</option>)}
          </select>
        </FormField>
      </div>
      <FormField id={f("durationMinutes").id} label="Duration (minutes)" error={f("durationMinutes").error}><Input id={f("durationMinutes").id} type="number" inputMode="numeric" min={15} step={15} {...f("durationMinutes").a11y} {...r("durationMinutes")} /></FormField>
      <FormField id={f("maxGuests").id} label="Max guests per departure" error={f("maxGuests").error}><Input id={f("maxGuests").id} type="number" inputMode="numeric" min={1} {...f("maxGuests").a11y} {...r("maxGuests")} /></FormField>
    </div>
  );
}

/** Booking mode and base prices (EUR). */
export function PricingFields<T extends PricingValues>({ prefix, register, errors, mode }: FieldProps<T> & { mode: PricingValues["bookingMode"] }) {
  const f = (name: keyof PricingValues) => field(prefix, errors, name);
  const r = (name: keyof PricingValues) => register(name as Path<T>);
  return (
    <div className="grid gap-4 sm:grid-cols-3">
      <FormField id={f("bookingMode").id} label="Booking" error={f("bookingMode").error}>
        <select id={f("bookingMode").id} className={selectClass} {...r("bookingMode")}>
          <option value="Shared">Shared seats</option>
          <option value="Private">Private trips</option>
          <option value="Both">Seats or private</option>
        </select>
      </FormField>
      {/* Only the prices the mode offers are shown; a hidden field keeps its value but isn't sent (see pricingBody). */}
      {mode !== "Private" && (
        <FormField id={f("pricePerPerson").id} label="Price per person (€)" error={f("pricePerPerson").error}>
          <Input id={f("pricePerPerson").id} type="number" inputMode="decimal" min={0} step="0.01" {...f("pricePerPerson").a11y} {...r("pricePerPerson")} />
        </FormField>
      )}
      {mode !== "Shared" && (
        <FormField id={f("privatePrice").id} label="Private trip price (€)" error={f("privatePrice").error}>
          <Input id={f("privatePrice").id} type="number" inputMode="decimal" min={0} step="0.01" {...f("privatePrice").a11y} {...r("privatePrice")} />
        </FormField>
      )}
    </div>
  );
}
