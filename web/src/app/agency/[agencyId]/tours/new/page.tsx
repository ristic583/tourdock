import type { Metadata } from "next";
import Link from "next/link";

import { CreateTourForm } from "@/components/agency/create-tour-form";
import { requireAgency } from "@/lib/agency";

// Private, per-user back-office data: rendered on demand, never prerendered.
export const instant = false;

export const metadata: Metadata = { title: "New tour" };

export default async function NewTourPage({ params }: { params: Promise<{ agencyId: string }> }) {
  const { agencyId } = await params;
  await requireAgency(agencyId, `/agency/${agencyId}/tours/new`);
  return (
    <div className="flex flex-col gap-6">
      <div>
        <Link href={`/agency/${agencyId}/tours`} className="text-sm font-semibold text-primary hover:text-primary-hover">← Tours</Link>
        <h1 className="mt-2 text-[32px] font-bold">New tour</h1>
        <p className="text-muted-foreground">It starts as a draft with a 72h / 24h cancellation policy. Add photos and a schedule next, then publish.</p>
      </div>
      <CreateTourForm agencyId={agencyId} />
    </div>
  );
}
