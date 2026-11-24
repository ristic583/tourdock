import type { Metadata } from "next";
import { Suspense } from "react";

import { ChangeEmailForm } from "@/components/account/change-email-form";
import { PageHeader } from "@/components/layout/page-header";
import { StatusPill, type StatusTone } from "@/components/ui/status-pill";
import type { AgencyStatus } from "@/lib/api/contracts";
import { getMyAgencies, requireUser } from "@/lib/auth/dal";

export const metadata: Metadata = { title: "Account", robots: { index: false } };

const AGENCY_STATUS: Record<AgencyStatus, { label: string; tone: StatusTone }> = {
  PendingApproval: { label: "Pending approval", tone: "pending" },
  Active: { label: "Active", tone: "confirmed" },
  Suspended: { label: "Suspended", tone: "cancelled" },
};

export default function AccountPage() {
  return (
    <div className="mx-auto max-w-page px-4 py-10 sm:px-6 sm:py-14">
      <PageHeader eyebrow="Account" title="Your profile" />
      <Suspense fallback={<div className="h-48 animate-pulse rounded-card bg-surface-2" />}>
        <AccountDetails />
      </Suspense>
    </div>
  );
}

async function AccountDetails() {
  const user = await requireUser("/account");
  const agencies = await getMyAgencies();

  return (
    <div className="flex flex-col gap-6 lg:flex-row lg:items-start">
      <section className="flex-1 rounded-card border border-line bg-surface p-6" aria-labelledby="profile-heading">
        <h2 id="profile-heading" className="text-xl font-bold">
          Profile
        </h2>
        <dl className="mt-4 grid gap-4 sm:grid-cols-2">
          <div>
            <dt className="label-caps">Name</dt>
            <dd className="mt-1 font-medium">{user.displayName}</dd>
          </div>
          <div>
            <dt className="label-caps">Email</dt>
            <dd className="mt-1 flex flex-wrap items-center gap-2 font-medium break-all">
              {user.email}
              <StatusPill tone={user.emailConfirmed ? "confirmed" : "pending"}>{user.emailConfirmed ? "Confirmed" : "Not confirmed"}</StatusPill>
            </dd>
          </div>
          {user.roles.length > 0 ? (
            <div>
              <dt className="label-caps">Roles</dt>
              <dd className="mt-1 font-medium">{user.roles.join(", ")}</dd>
            </div>
          ) : null}
        </dl>
        <div className="mt-6 flex flex-col gap-3 border-t border-line pt-5">
          <h3 className="font-semibold">Email address</h3>
          <ChangeEmailForm currentEmail={user.email} />
        </div>
      </section>

      <section className="rounded-card border border-line bg-surface p-6 lg:w-[380px]" aria-labelledby="agencies-heading">
        <h2 id="agencies-heading" className="text-xl font-bold">
          Agencies
        </h2>
        {agencies.length === 0 ? (
          <p className="mt-3 text-muted-foreground">You don&apos;t work for an agency yet.</p>
        ) : (
          <ul className="mt-4 flex flex-col divide-y divide-line">
            {agencies.map((agency) => (
              <li key={agency.id} className="flex items-center justify-between gap-3 py-3 first:pt-0 last:pb-0">
                <div className="min-w-0">
                  <p className="truncate font-semibold">{agency.name}</p>
                  <p className="text-sm text-muted-foreground">{agency.myRole}</p>
                </div>
                <StatusPill tone={AGENCY_STATUS[agency.status].tone}>{AGENCY_STATUS[agency.status].label}</StatusPill>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
