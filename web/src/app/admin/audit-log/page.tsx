import type { Metadata } from "next";
import Link from "next/link";

import { AdminTabs } from "@/components/admin/admin-tabs";
import type { AdminActionEntry, PagedResponse } from "@/lib/api/contracts";
import { unwrap } from "@/lib/api/client";
import { serverApi } from "@/lib/api/server";
import { requireAdmin } from "@/lib/auth/dal";
import { readAccessToken } from "@/lib/auth/session";

export const metadata: Metadata = { title: "Audit log · Admin", robots: { index: false } };
export const instant = false;

const PAGE_SIZE = 50;
const ACTIONS: Record<string, string> = {
  "Agency.Approve": "Approved agency",
  "Agency.Suspend": "Suspended agency",
  "Review.Hide": "Hid review",
  "Review.Unhide": "Showed review again",
  "Review.DismissReports": "Closed reports on review",
  "User.ConfirmEmail": "Confirmed user's email",
  "Refund.Retry": "Sent a failed refund again",
};

type Props = { searchParams: Promise<{ page?: string }> };

export default async function AuditLogPage({ searchParams }: Props) {
  await requireAdmin("/admin/audit-log");
  const page = Math.max(1, Number.parseInt((await searchParams).page ?? "1", 10) || 1);
  const result = await unwrap<PagedResponse<AdminActionEntry>>(
    serverApi(await readAccessToken()).GET("/api/v1/admin/audit-log", { params: { query: { page, pageSize: PAGE_SIZE } } }),
  );
  const when = (value: string) => new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit", hourCycle: "h23", timeZone: "UTC" }).format(new Date(value));

  return (
    <div className="mx-auto max-w-page px-4 pt-8 pb-20 sm:px-6">
      <p className="label-caps">Admin</p>
      <h1 className="mt-1 text-[32px] font-bold">Audit log</h1>
      <AdminTabs current="/admin/audit-log" />
      <p className="mt-4 mb-5 text-sm text-muted-foreground">Every admin action, newest first. Entries are never changed or deleted. Times in UTC.</p>
      {result.items.length ? (
        <div className="relative overflow-x-auto rounded-card border border-line bg-surface">
          <table className="w-full min-w-[720px] text-left text-sm">
            <thead className="border-b border-line text-xs text-muted-foreground">
              <tr>
                <th scope="col" className="px-4 py-3 font-semibold">When</th>
                <th scope="col" className="font-semibold">Admin</th>
                <th scope="col" className="font-semibold">Action</th>
                <th scope="col" className="font-semibold">Item</th>
                <th scope="col" className="pr-4 font-semibold">Reason</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {result.items.map((entry) => (
                <tr key={entry.id}>
                  <td className="px-4 py-3 whitespace-nowrap">{when(entry.at)}</td>
                  <td className="pr-3">{entry.adminName}</td>
                  <td className="pr-3 font-medium">{ACTIONS[entry.action] ?? entry.action}</td>
                  <td className="pr-3"><AuditTarget entry={entry} /></td>
                  <td className="max-w-sm pr-4 text-muted-foreground">{entry.reason ?? "–"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="rounded-card border border-dashed border-line bg-surface p-6 text-muted-foreground">No admin actions yet.</p>
      )}
      {result.totalPages > 1 && (
        <nav aria-label="Pagination" className="mt-6 flex items-center justify-center gap-3 text-sm">
          {result.page > 1 && <Link href={`/admin/audit-log?page=${result.page - 1}`} className="font-semibold text-primary">← Newer</Link>}
          <span className="text-muted-foreground">Page {result.page} of {result.totalPages}</span>
          {result.page < result.totalPages && <Link href={`/admin/audit-log?page=${result.page + 1}`} className="font-semibold text-primary">Older →</Link>}
        </nav>
      )}
    </div>
  );
}

/** The readable name of what was changed, linked where there is a page for it; the id stays available as a tooltip. */
function AuditTarget({ entry }: { entry: AdminActionEntry }) {
  // Refunds have no name or page of their own; the reason in the row says what happened.
  const name = entry.targetName ?? (entry.targetType === "Refund" ? "Refund to a traveler" : `${entry.targetType} (deleted)`);
  const href = entry.targetType === "User" ? undefined : entry.targetType === "Agency"
    ? "/admin/agencies?status=all"
    : entry.tourId ? `/tours/${entry.tourId}#reviews-heading` : undefined;
  const label = <span title={`${entry.targetType} ${entry.targetId}`}>{name}</span>;
  return (
    <span className="flex flex-col">
      {href ? <Link href={href} className="font-medium text-primary hover:text-primary-hover">{label}</Link> : <span className="font-medium">{label}</span>}
      <span className="text-xs text-muted-foreground">{entry.targetType}</span>
    </span>
  );
}
