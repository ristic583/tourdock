"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useRouter } from "next/navigation";
import { useState, type ReactNode } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";

import { FormAlert } from "@/components/feedback/form-alert";
import { useToast } from "@/components/feedback/toaster";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { Dialog, DialogContent, DialogDescription, DialogTitle, DialogTrigger } from "@/components/ui/dialog";
import { FormField, describedBy } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { agencyApi } from "@/lib/api/agency-client";
import type { Vehicle, VehicleType } from "@/lib/api/contracts";
import { toActionError } from "@/lib/api/problem";
import { applyActionError } from "@/lib/forms";

export const VEHICLE_TYPES: VehicleType[] = ["Boat", "Car", "Jeep", "Bus", "Other"];

const schema = z.object({
  name: z.string().trim().min(1, "Give the vehicle a name.").max(100),
  type: z.enum(["Boat", "Car", "Jeep", "Bus", "Other"]),
  capacity: z.coerce.number<number>({ error: "Enter the number of seats." }).int("Whole seats only.").min(1, "At least 1 seat.").max(500, "At most 500 seats."),
});
type Values = z.infer<typeof schema>;

/** Add or edit a vehicle. */
export function VehicleDialog({ agencyId, vehicle, trigger }: { agencyId: string; vehicle?: Vehicle; trigger: ReactNode }) {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { name: vehicle?.name ?? "", type: vehicle?.type ?? "Boat", capacity: vehicle?.capacity ?? 8 },
  });
  const { errors, isSubmitting } = form.formState;
  const onSubmit = form.handleSubmit(async (values) => {
    try {
      if (vehicle) await agencyApi.updateVehicle(agencyId, vehicle.id, values);
      else await agencyApi.createVehicle(agencyId, values);
      setOpen(false);
      form.reset(values);
      router.refresh();
    } catch (error) {
      applyActionError(toActionError(error), form.setError, ["name", "type", "capacity"]);
    }
  });

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>{trigger}</DialogTrigger>
      <DialogContent>
        <div>
          <DialogTitle>{vehicle ? `Edit ${vehicle.name}` : "Add a vehicle"}</DialogTitle>
          <DialogDescription className="mt-1">Departures run on a vehicle; its seats are the departure&apos;s capacity.</DialogDescription>
        </div>
        <form onSubmit={onSubmit} noValidate className="flex flex-col gap-4">
          <FormAlert message={errors.root?.server?.message} />
          <FormField id="vehicle-name" label="Name" error={errors.name?.message}>
            <Input id="vehicle-name" aria-invalid={Boolean(errors.name)} aria-describedby={describedBy("vehicle-name", errors.name?.message)} {...form.register("name")} />
          </FormField>
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField id="vehicle-type" label="Type" error={errors.type?.message}>
              <select id="vehicle-type" className="h-11 rounded-control border border-line bg-surface px-3" {...form.register("type")}>
                {VEHICLE_TYPES.map((type) => <option key={type} value={type}>{type}</option>)}
              </select>
            </FormField>
            <FormField id="vehicle-capacity" label="Seats" error={errors.capacity?.message}>
              <Input id="vehicle-capacity" type="number" inputMode="numeric" min={1} aria-invalid={Boolean(errors.capacity)} aria-describedby={describedBy("vehicle-capacity", errors.capacity?.message)} {...form.register("capacity")} />
            </FormField>
          </div>
          <Button type="submit" pending={isSubmitting}>{isSubmitting ? "Saving…" : vehicle ? "Save changes" : "Add vehicle"}</Button>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/** Delete with a confirmation step. The API refuses when the vehicle still has departures. */
export function DeleteVehicleButton({ agencyId, vehicle }: { agencyId: string; vehicle: Vehicle }) {
  const router = useRouter();
  const toast = useToast();
  return (
    <ConfirmDialog
      tone="danger"
      trigger={<Button variant="ghost" size="sm">Delete<span className="sr-only"> {vehicle.name}</span></Button>}
      title={`Delete ${vehicle.name}?`}
      description="This can't be undone. A vehicle with departures can't be deleted; cancel or move those first."
      confirmLabel="Delete vehicle"
      pendingLabel="Deleting…"
      errorFallback="We couldn't delete this vehicle."
      onConfirm={() => agencyApi.deleteVehicle(agencyId, vehicle.id)}
      onDone={() => {
        toast(`${vehicle.name} is deleted.`);
        router.refresh();
      }}
    />
  );
}
