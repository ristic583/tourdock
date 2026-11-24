import type { Metadata } from "next";
import Link from "next/link";
import { redirect } from "next/navigation";

import { RegisterAgencyForm } from "@/components/agency/register-agency-form";
import { StatusPill } from "@/components/ui/status-pill";
import { getMyAgencies, requireUser } from "@/lib/auth/dal";

export const metadata: Metadata = { title: "Agency back office", robots: { index: false } };
export const instant = false;

const STATUS = { Active: ["confirmed", "Active"], PendingApproval: ["pending", "Pending approval"], Suspended: ["cancelled", "Suspended"] } as const;

/** Entry to the back office: straight into your agency, a choice when you work for several, or registration. */
export default async function AgencyIndexPage() {
  await requireUser("/agency");
  const agencies = await getMyAgencies();
  if (agencies.length === 1) redirect(`/agency/${agencies[0].id}`);

  return (
    <div className="mx-auto max-w-3xl px-4 py-12 sm:px-6">
      <p className="label-caps">For agencies</p>
      <h1 className="mt-1 text-4xl font-bold">Agency back office</h1>
      {agencies.length ? (
        <ul className="mt-8 flex flex-col gap-3">
          {agencies.map((agency) => (
            <li key={agency.id}>
              <Link href={`/agency/${agency.id}`} className="flex items-center justify-between gap-4 rounded-card border border-line bg-surface p-5 text-ink hover:border-muted-foreground">
                <span><span className="block font-semibold">{agency.name}</span><span className="text-sm text-muted-foreground">{agency.myRole}</span></span>
                <StatusPill tone={STATUS[agency.status][0]}>{STATUS[agency.status][1]}</StatusPill>
              </Link>
            </li>
          ))}
        </ul>
      ) : (
        <>
          <p className="mt-3 max-w-xl text-muted-foreground">
            List your boat trips, fishing charters or safaris. Register your agency; you become its manager, and a TourDock
            admin approves it before your tours go on sale.
          </p>
          <div className="mt-8"><RegisterAgencyForm /></div>
        </>
      )}
    </div>
  );
}
