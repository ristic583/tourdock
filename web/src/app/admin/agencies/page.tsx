import type { Metadata } from "next";
import Link from "next/link";

import { AdminTabs } from "@/components/admin/admin-tabs";
import { AgencyActions } from "@/components/admin/agency-actions";
import { StatusPill, type StatusTone } from "@/components/ui/status-pill";
import type { AdminAgency, AgencyStatus, PagedResponse } from "@/lib/api/contracts";
import { unwrap } from "@/lib/api/client";
import { serverApi } from "@/lib/api/server";
import { requireAdmin } from "@/lib/auth/dal";
import { readAccessToken } from "@/lib/auth/session";
import { cn } from "@/lib/utils";

export const metadata: Metadata = { title: "Agencies · Admin", robots: { index: false } };
export const instant = false;

const PAGE_SIZE = 20;
const FILTERS: { value?: AgencyStatus; label: string }[] = [
  { value: "PendingApproval", label: "Pending approval" },
  { value: "Active", label: "Active" },
  { value: "Suspended", label: "Suspended" },
  { value: undefined, label: "All" },
];
const STATUS: Record<AgencyStatus, { tone: StatusTone; label: string }> = {
  PendingApproval: { tone: "pending", label: "Pending approval" },
  Active: { tone: "confirmed", label: "Active" },
  Suspended: { tone: "cancelled", label: "Suspended" },
};

type Props = { searchParams: Promise<{ status?: string; page?: string }> };

export default async function AdminAgenciesPage({ searchParams }: Props) {
  await requireAdmin("/admin/agencies");
  const query = await searchParams;
  // Pending approval first: that's the admin's to-do list.
  const status = query.status === "all" ? undefined : (FILTERS.find((filter) => filter.value === query.status)?.value ?? "PendingApproval");
  const page = Math.max(1, Number.parseInt(query.page ?? "1", 10) || 1);
  const result = await unwrap<PagedResponse<AdminAgency>>(
    serverApi(await readAccessToken()).GET("/api/v1/admin/agencies", { params: { query: { status, page, pageSize: PAGE_SIZE } } }),
  );
  const href = (changes: { status?: AgencyStatus | "all"; page?: number }) => {
    const next = new URLSearchParams({ status: changes.status ?? status ?? "all", page: String(changes.page ?? 1) });
    return `/admin/agencies?${next}`;
  };
  const date = (value: string) => new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric", timeZone: "UTC" }).format(new Date(value));

  return (
    <div className="mx-auto max-w-page px-4 pt-8 pb-20 sm:px-6">
      <p className="label-caps">Admin</p>
      <h1 className="mt-1 text-[32px] font-bold">Agencies</h1>
      <AdminTabs current="/admin/agencies" />
      <nav aria-label="Status filter" className="mt-5 mb-5 flex flex-wrap gap-2">
        {FILTERS.map((filter) => {
          const on = filter.value === status;
          return (
            <Link key={filter.label} href={href({ status: filter.value ?? "all" })} aria-current={on ? "page" : undefined} className={cn("inline-flex min-h-9 items-center rounded-full border px-3.5 text-sm font-medium", on ? "border-ink bg-ink text-bg" : "border-line bg-surface text-ink hover:border-muted-foreground")}>
              {filter.label}
            </Link>
          );
        })}
      </nav>

      {result.items.length ? (
        <div className="relative overflow-x-auto rounded-card border border-line bg-surface">
          <table className="w-full min-w-[760px] text-left text-sm">
            <thead className="border-b border-line text-xs text-muted-foreground">
              <tr>
                <th scope="col" className="px-4 py-3 font-semibold">Agency</th>
                <th scope="col" className="font-semibold">Registered</th>
                <th scope="col" className="font-semibold">Managers</th>
                <th scope="col" className="font-semibold">Status</th>
                <th scope="col"><span className="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {result.items.map((agency) => (
                <tr key={agency.id}>
                  <td className="px-4 py-3"><span className="block font-semibold">{agency.name}</span><span className="text-xs text-muted-foreground">{agency.contactEmail}</span></td>
                  <td className="pr-3 whitespace-nowrap">{date(agency.createdAt)}</td>
                  <td className="pr-3">{agency.managerCount}</td>
                  <td className="pr-3">
                    <StatusPill tone={STATUS[agency.status].tone}>{STATUS[agency.status].label}</StatusPill>
                    {agency.suspensionReason && <span className="mt-1 block max-w-xs text-xs text-muted-foreground">“{agency.suspensionReason}”</span>}
                  </td>
                  <td className="pr-4 py-3"><AgencyActions agency={agency} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="rounded-card border border-dashed border-line bg-surface p-6 text-muted-foreground">No agencies here.</p>
      )}

      {result.totalPages > 1 && (
        <nav aria-label="Pagination" className="mt-6 flex items-center justify-center gap-3 text-sm">
          {result.page > 1 && <Link href={href({ page: result.page - 1 })} className="font-semibold text-primary">← Previous</Link>}
          <span className="text-muted-foreground">Page {result.page} of {result.totalPages} · {result.totalCount} agencies</span>
          {result.page < result.totalPages && <Link href={href({ page: result.page + 1 })} className="font-semibold text-primary">Next →</Link>}
        </nav>
      )}
    </div>
  );
}
