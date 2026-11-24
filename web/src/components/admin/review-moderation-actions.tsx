"use client";

import { useRouter } from "next/navigation";
import { useId, useState } from "react";

import { useToast } from "@/components/feedback/toaster";
import { Button } from "@/components/ui/button";
import { ConfirmDialog, ReasonField } from "@/components/ui/confirm-dialog";
import type { ModerationReview } from "@/lib/api/contracts";
import { reviewsApi } from "@/lib/api/reviews-client";

/** Hide (with a reason), show again, or keep a review and close its reports. Every action asks first and lands in the audit log. */
export function ReviewModerationActions({ review }: { review: ModerationReview }) {
  const router = useRouter();
  const toast = useToast();
  const reasonId = useId();
  const [reason, setReason] = useState("");
  const done = (message: string) => () => {
    toast(message);
    router.refresh();
  };

  return (
    <div className="flex flex-wrap justify-end gap-2">
      {review.isHidden ? (
        <ConfirmDialog
          trigger={<Button size="sm" variant="outline">Show again</Button>}
          title="Show this review again?"
          description="It comes back on the tour page and counts in the rating again."
          confirmLabel="Show review"
          pendingLabel="Showing…"
          onConfirm={() => reviewsApi.unhide(review.id)}
          onDone={done("The review is visible again.")}
        />
      ) : (
        <ConfirmDialog
          tone="danger"
          trigger={<Button size="sm" variant="urgent">Hide</Button>}
          title="Hide this review?"
          description="It disappears from the tour page and the rating. The author and the agency see that it was hidden. Open reports are closed."
          confirmLabel="Hide review"
          pendingLabel="Hiding…"
          confirmDisabled={reason.trim().length < 3}
          onConfirm={() => reviewsApi.hide(review.id, reason.trim())}
          onDone={() => {
            setReason("");
            done("The review is hidden.")();
          }}
          onCancel={() => setReason("")}
        >
          <ReasonField id={reasonId} label="Reason (kept in the audit log)" value={reason} onChange={setReason} />
        </ConfirmDialog>
      )}
      {review.openReports > 0 && (
        <ConfirmDialog
          trigger={<Button size="sm" variant="outline">Keep it, close reports</Button>}
          title="Keep this review?"
          description={`It stays visible, and ${review.openReports === 1 ? "the open report is" : `all ${review.openReports} open reports are`} closed.`}
          confirmLabel="Keep and close reports"
          pendingLabel="Closing…"
          onConfirm={() => reviewsApi.dismissReports(review.id)}
          onDone={done("Reports closed. The review stays visible.")}
        />
      )}
    </div>
  );
}
