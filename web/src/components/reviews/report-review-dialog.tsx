"use client";

import { Flag } from "lucide-react";
import { useId, useState } from "react";

import { FormAlert } from "@/components/feedback/form-alert";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogTitle, DialogTrigger } from "@/components/ui/dialog";
import type { ReportReason } from "@/lib/api/contracts";
import { ApiError } from "@/lib/api/problem";
import { reviewsApi } from "@/lib/api/reviews-client";
import { REPORT_REASONS } from "@/lib/reviews";

/** Flags a review for the moderators. It stays visible until an admin decides. */
export function ReportReviewDialog({ reviewId }: { reviewId: string }) {
  const noteId = useId();
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState<ReportReason>("Spam");
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [done, setDone] = useState(false);

  async function submit() {
    setBusy(true);
    setError(undefined);
    try {
      await reviewsApi.report(reviewId, reason, note.trim() || undefined);
      setDone(true);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.userMessage : "We couldn't send your report. Please try again.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={(next) => { setOpen(next); if (!next) { setDone(false); setError(undefined); } }}>
      <DialogTrigger asChild>
        <button type="button" className="inline-flex items-center gap-1 text-[13px] text-muted-foreground hover:text-ink">
          <Flag className="size-3.5" aria-hidden /> Report
        </button>
      </DialogTrigger>
      <DialogContent>
        <div>
          <DialogTitle>Report this review</DialogTitle>
          <DialogDescription className="mt-1">Our moderators will take a look. The review stays up until they decide.</DialogDescription>
        </div>
        {done ? (
          <>
            <p role="status" className="text-sm font-medium">Thanks, we&apos;ve got your report.</p>
            <Button variant="outline" onClick={() => setOpen(false)}>Close</Button>
          </>
        ) : (
          <>
            <fieldset className="flex flex-col gap-2">
              <legend className="mb-1 text-sm font-semibold">What&apos;s wrong with it?</legend>
              {REPORT_REASONS.map((option) => (
                <label key={option.value} className="flex items-center gap-2 text-sm">
                  <input type="radio" name="report-reason" value={option.value} checked={reason === option.value} onChange={() => setReason(option.value)} />
                  {option.label}
                </label>
              ))}
            </fieldset>
            <label htmlFor={noteId} className="flex flex-col gap-1.5 text-sm font-semibold">
              Anything to add? <span className="font-normal text-muted-foreground">(optional)</span>
              <textarea id={noteId} rows={3} maxLength={500} value={note} onChange={(event) => setNote(event.target.value)} className="rounded-control border border-line bg-surface px-3 py-2 font-normal" />
            </label>
            <FormAlert message={error} />
            <Button pending={busy} onClick={submit}>{busy ? "Sending…" : "Send report"}</Button>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}
