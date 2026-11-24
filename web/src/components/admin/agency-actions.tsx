"use client";

import { useRouter } from "next/navigation";
import { useId, useState } from "react";

import { useToast } from "@/components/feedback/toaster";
import { Button } from "@/components/ui/button";
import { ConfirmDialog, ReasonField } from "@/components/ui/confirm-dialog";
import { adminApi } from "@/lib/api/agency-client";
import type { AdminAgency } from "@/lib/api/contracts";

/** Approve a pending agency, or suspend one (a reason is required and shown to its managers). Both ask first. */
export function AgencyActions({ agency }: { agency: AdminAgency }) {
  const router = useRouter();
  const toast = useToast();
  const reasonId = useId();
  const [reason, setReason] = useState("");

  return (
    <div className="flex flex-wrap justify-end gap-2">
      {agency.status === "PendingApproval" && (
        <ConfirmDialog
          trigger={<Button size="sm">Approve</Button>}
          title={`Approve ${agency.name}?`}
          description="Its managers can publish tours right away, and we email them that the agency is live."
          confirmLabel="Approve agency"
          pendingLabel="Approving…"
          onConfirm={() => adminApi.approve(agency.id)}
          onDone={() => {
            toast(`${agency.name} is approved.`);
            router.refresh();
          }}
        />
      )}
      {agency.status !== "Suspended" && (
        <ConfirmDialog
          tone="danger"
          trigger={<Button size="sm" variant="outline">Suspend</Button>}
          title={`Suspend ${agency.name}?`}
          description="Its tours are hidden from travelers at once. Its managers see your reason by email and in the app."
          confirmLabel="Suspend agency"
          pendingLabel="Suspending…"
          confirmDisabled={reason.trim().length < 3}
          onConfirm={() => adminApi.suspend(agency.id, reason.trim())}
          onDone={() => {
            setReason("");
            toast(`${agency.name} is suspended.`);
            router.refresh();
          }}
          onCancel={() => setReason("")}
        >
          <ReasonField id={reasonId} label="Reason" value={reason} onChange={setReason} />
        </ConfirmDialog>
      )}
    </div>
  );
}
