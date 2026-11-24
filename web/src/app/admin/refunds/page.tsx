import type { Metadata } from "next";

import { AdminTabs } from "@/components/admin/admin-tabs";
import { RetryRefundAction } from "@/components/admin/retry-refund-action";
import type { FailedRefund } from "@/lib/api/contracts";
import { unwrap } from "@/lib/api/client";
import { serverApi } from "@/lib/api/server";
import { requireAdmin } from "@/lib/auth/dal";
import { readAccessToken } from "@/lib/auth/session";
import { formatMoney } from "@/lib/format";

export const metadata: Metadata = { title: "Refunds · Admin", robots: { index: false } };
export const instant = false;

const when = (value: string) =>
  new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit", hourCycle: "h23", timeZone: "UTC" })
    .format(new Date(value));

/** Refunds the payment provider declined three times: the traveler is still owed the money until someone acts. */
export default async function AdminRefundsPage() {
  await requireAdmin("/admin/refunds");
  const refunds = await unwrap<FailedRefund[]>(serverApi(await readAccessToken()).GET("/api/v1/admin/refunds"));

  return (
    <div className="mx-auto max-w-page px-4 pt-8 pb-20 sm:px-6">
      <p className="label-caps">Admin</p>
      <h1 className="mt-1 text-[32px] font-bold">Refunds</h1>
      <AdminTabs current="/admin/refunds" />
      <p className="mt-4 mb-5 text-sm text-muted-foreground">
        Refunds the payment provider declined three times. Check the reason with the traveler (e.g. a closed card), then send
        it again. Times in UTC.
      </p>
      {refunds.length ? (
        <ul className="flex flex-col gap-3">
          {refunds.map((refund) => (
            <li key={refund.refundId} className="flex flex-col gap-3 rounded-card border border-line bg-surface p-4 sm:flex-row sm:items-center">
              <div className="min-w-0 flex-1">
                <p className="font-semibold">{formatMoney(refund.amount)} · {refund.reason}</p>
                <p className="text-sm text-muted-foreground">
                  Declined {refund.failedAttempts}× · last {when(refund.failedAt)} · {refund.failureReason ?? "No reason given"}
                </p>
                <p className="truncate text-xs text-muted-foreground" title={refund.bookingId}>Booking {refund.bookingId}</p>
              </div>
              <RetryRefundAction refund={refund} />
            </li>
          ))}
        </ul>
      ) : (
        <p className="rounded-card border border-dashed border-line bg-surface p-6 text-muted-foreground">No failed refunds. Every traveler got their money back.</p>
      )}
    </div>
  );
}
