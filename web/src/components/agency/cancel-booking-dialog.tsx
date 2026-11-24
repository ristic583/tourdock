"use client";

import { useRouter } from "next/navigation";
import { useId, useState } from "react";

import { useToast } from "@/components/feedback/toaster";
import { Button } from "@/components/ui/button";
import { ConfirmDialog, ReasonField } from "@/components/ui/confirm-dialog";
import { agencyApi } from "@/lib/api/agency-client";

/** Agency-side cancellation of one booking. The traveler gets a full refund; a reason is required. */
export function CancelBookingDialog({ agencyId, bookingId, label }: { agencyId: string; bookingId: string; label: string }) {
  const router = useRouter();
  const toast = useToast();
  const reasonId = useId();
  const [reason, setReason] = useState("");

  return (
    <ConfirmDialog
      tone="danger"
      trigger={<Button variant="outline" size="sm">Cancel<span className="sr-only"> booking {label}</span></Button>}
      title="Cancel this booking?"
      description={`${label}. The traveler is refunded in full and sees your reason by email.`}
      confirmLabel="Cancel and refund"
      pendingLabel="Cancelling…"
      cancelLabel="Keep booking"
      confirmDisabled={reason.trim().length < 3}
      errorFallback="We couldn't cancel this booking."
      onConfirm={() => agencyApi.cancelBooking(agencyId, bookingId, reason.trim())}
      onDone={() => {
        setReason("");
        toast("Booking cancelled. The traveler is refunded in full.");
        router.refresh();
      }}
      onCancel={() => setReason("")}
    >
      <ReasonField id={reasonId} label="Reason (shown to the traveler)" value={reason} onChange={setReason} />
    </ConfirmDialog>
  );
}
