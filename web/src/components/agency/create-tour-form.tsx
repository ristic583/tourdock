"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useRouter } from "next/navigation";
import { useForm, useWatch } from "react-hook-form";

import { DetailsFields, PricingFields } from "@/components/agency/tour-fields";
import { FormAlert } from "@/components/feedback/form-alert";
import { Button } from "@/components/ui/button";
import { agencyApi } from "@/lib/api/agency-client";
import { toActionError } from "@/lib/api/problem";
import { applyActionError } from "@/lib/forms";
import { createTourSchema, detailsBody, detailsDefaults, pricingBody, pricingDefaults, type DetailsValues, type PricingValues } from "@/lib/tour-forms";

type Values = DetailsValues & PricingValues;
const FIELDS = ["title", "description", "countryCode", "city", "address", "latitude", "longitude", "timeZoneId", "durationMinutes", "maxGuests", "bookingMode", "pricePerPerson", "privatePrice"] as const;

/** A new tour starts as a draft with the default cancellation policy; photos, schedules and seasons come next. */
export function CreateTourForm({ agencyId }: { agencyId: string }) {
  const router = useRouter();
  const form = useForm<Values>({ resolver: zodResolver(createTourSchema), defaultValues: { ...detailsDefaults(), ...pricingDefaults() } });
  const { errors, isSubmitting } = form.formState;
  const mode = useWatch({ control: form.control, name: "bookingMode" });

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const tour = await agencyApi.createTour(agencyId, { ...detailsBody(values), ...pricingBody(values), cancellationPolicy: null });
      router.push(`/agency/${agencyId}/tours/${tour.id}`);
    } catch (error) {
      applyActionError(toActionError(error), form.setError, FIELDS);
    }
  });

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-6">
      <FormAlert message={errors.root?.server?.message} />
      <section className="flex flex-col gap-4 rounded-card border border-line bg-surface p-5">
        <h2 className="text-xl font-bold">Details</h2>
        <DetailsFields prefix="new-tour" form={form} />
      </section>
      <section className="flex flex-col gap-4 rounded-card border border-line bg-surface p-5">
        <h2 className="text-xl font-bold">Pricing</h2>
        <PricingFields prefix="new-tour" register={form.register} errors={errors} mode={mode} />
      </section>
      <Button type="submit" className="self-start" pending={isSubmitting}>{isSubmitting ? "Creating…" : "Create draft"}</Button>
    </form>
  );
}
