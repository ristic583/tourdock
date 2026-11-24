"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { z } from "zod";

import { FormAlert } from "@/components/feedback/form-alert";
import { Button } from "@/components/ui/button";
import { FormField, describedBy } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { agencyApi } from "@/lib/api/agency-client";
import { toActionError } from "@/lib/api/problem";
import { applyActionError } from "@/lib/forms";

const schema = z.object({
  name: z.string().trim().min(2, "Enter the agency's name.").max(200),
  contactEmail: z.email("Enter a valid email."),
});
type Input = z.infer<typeof schema>;

export function RegisterAgencyForm() {
  const router = useRouter();
  const form = useForm<Input>({ resolver: zodResolver(schema), defaultValues: { name: "", contactEmail: "" } });
  const { errors, isSubmitting } = form.formState;
  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const agency = await agencyApi.register(values);
      router.push(`/agency/${agency.id}`);
      router.refresh();
    } catch (error) {
      applyActionError(toActionError(error), form.setError, ["name", "contactEmail"]);
    }
  });
  return (
    <form onSubmit={onSubmit} noValidate className="flex max-w-md flex-col gap-4">
      <FormAlert message={errors.root?.server?.message} />
      <FormField id="agency-name" label="Agency name" error={errors.name?.message}>
        <Input id="agency-name" aria-invalid={Boolean(errors.name)} aria-describedby={describedBy("agency-name", errors.name?.message)} {...form.register("name")} />
      </FormField>
      <FormField id="agency-email" label="Contact email" error={errors.contactEmail?.message} hint="Travelers and TourDock reach you here.">
        <Input id="agency-email" type="email" autoComplete="email" aria-invalid={Boolean(errors.contactEmail)} aria-describedby={describedBy("agency-email", errors.contactEmail?.message, "Travelers and TourDock reach you here.")} {...form.register("contactEmail")} />
      </FormField>
      <Button type="submit" pending={isSubmitting}>{isSubmitting ? "Registering…" : "Register agency"}</Button>
    </form>
  );
}
