"use client";

import { useRouter } from "next/navigation";
import { useId, useState } from "react";

import { useToast } from "@/components/feedback/toaster";
import { Button } from "@/components/ui/button";
import { ConfirmDialog, ReasonField } from "@/components/ui/confirm-dialog";
import { adminApi } from "@/lib/api/agency-client";
import type { FailedRefund } from "@/lib/api/contracts";
import { formatMoney } from "@/lib/format";

/** Sends a failed refund to the provider once more; a note (what changed) goes to the audit log. */
export function RetryRefundAction({ refund }: { refund: FailedRefund }) {
  const router = useRouter();
  const toast = useToast();
  const noteId = useId();
  const [note, setNote] = useState("");

  return (
    <ConfirmDialog
      trigger={<Button size="sm">Send again</Button>}
      title={`Send the ${formatMoney(refund.amount)} refund again?`}
      description="The payment provider gets a new refund request. Recorded in the audit log."
      confirmLabel="Send refund"
      pendingLabel="Sending…"
      confirmDisabled={note.trim().length < 3}
      onConfirm={() => adminApi.retryRefund(refund.refundId, note.trim())}
      onDone={() => {
        setNote("");
        toast("The refund was sent again.");
        router.refresh();
      }}
      onCancel={() => setNote("")}
    >
      <ReasonField id={noteId} label="What changed?" value={note} onChange={setNote} />
    </ConfirmDialog>
  );
}
