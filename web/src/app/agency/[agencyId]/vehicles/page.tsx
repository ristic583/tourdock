import type { Metadata } from "next";

import { DeleteVehicleButton, VehicleDialog } from "@/components/agency/vehicle-dialog";
import { Button } from "@/components/ui/button";
import { listVehicles, requireAgency } from "@/lib/agency";

// Private, per-user back-office data: rendered on demand, never prerendered.
export const instant = false;

export const metadata: Metadata = { title: "Vehicles" };

type Props = { params: Promise<{ agencyId: string }> };

export default async function VehiclesPage({ params }: Props) {
  const { agencyId } = await params;
  await requireAgency(agencyId, `/agency/${agencyId}/vehicles`);
  const vehicles = await listVehicles(agencyId);
  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-[32px] font-bold">Vehicles</h1>
          <p className="text-muted-foreground">Boats, jeeps and buses your departures run on. One vehicle never runs two departures at once.</p>
        </div>
        <VehicleDialog agencyId={agencyId} trigger={<Button>+ Add vehicle</Button>} />
      </div>
      {vehicles.length ? (
        <ul className="divide-y divide-line rounded-card border border-line bg-surface">
          {vehicles.map((vehicle) => (
            <li key={vehicle.id} className="flex flex-wrap items-center gap-4 px-5 py-4">
              <div className="min-w-0 flex-1">
                <p className="font-semibold">{vehicle.name}</p>
                <p className="text-sm text-muted-foreground">{vehicle.type} · {vehicle.capacity} seats</p>
              </div>
              <VehicleDialog agencyId={agencyId} vehicle={vehicle} trigger={<Button variant="outline" size="sm">Edit</Button>} />
              <DeleteVehicleButton agencyId={agencyId} vehicle={vehicle} />
            </li>
          ))}
        </ul>
      ) : (
        <p className="rounded-card border border-dashed border-line bg-surface p-6 text-muted-foreground">No vehicles yet. Add one to start scheduling departures.</p>
      )}
    </div>
  );
}
