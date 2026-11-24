import type { Metadata } from "next";
import type { ReactNode } from "react";

import { AgencyNav } from "@/components/agency/agency-nav";
import { requireAgency } from "@/lib/agency";
import { getMyAgencies } from "@/lib/auth/dal";

// Private, per-user back-office data: rendered on demand, never prerendered.
export const instant = false;

export const metadata: Metadata = { robots: { index: false } };

const STATUS_TEXT = {
  PendingApproval: "This agency is waiting for approval by a TourDock admin. You can prepare vehicles and tours; they go on sale once it's approved.",
  Suspended: "This agency is suspended. Its tours are hidden from travelers until an admin reinstates it.",
} as const;

export default async function AgencyLayout({ children, params }: { children: ReactNode; params: Promise<{ agencyId: string }> }) {
  const { agencyId } = await params;
  const agency = await requireAgency(agencyId, `/agency/${agencyId}`);
  const agencies = await getMyAgencies();
  const notice = agency.status === "Active" ? undefined : STATUS_TEXT[agency.status];
  return (
    <div className="mx-auto grid max-w-page grid-cols-[minmax(0,1fr)] gap-6 px-4 pt-6 pb-20 sm:px-6 lg:grid-cols-[240px_minmax(0,1fr)] lg:gap-8 lg:pt-8">
      <AgencyNav agency={agency} agencies={agencies} />
      <div className="min-w-0">
        {notice && (
          <p role="status" className="mb-6 rounded-control bg-[var(--status-pending-bg)] px-4 py-3 text-sm font-medium text-[var(--status-pending-ink)]">{notice}</p>
        )}
        {children}
      </div>
    </div>
  );
}
