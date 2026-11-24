"use client";

import { useRouter } from "next/navigation";
import { useId, useState } from "react";

import { FormAlert } from "@/components/feedback/form-alert";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogTitle, DialogTrigger } from "@/components/ui/dialog";
import type { MyReview } from "@/lib/api/contracts";
import { ApiError } from "@/lib/api/problem";
import { reviewsApi } from "@/lib/api/reviews-client";

import { StarInput } from "./stars";

const MAX = 2000;

/**
 * Write, change or delete the review of a completed booking. Changing is only offered while the API allows it
 * (14 days after posting); deleting always is.
 */
export function ReviewDialog({ bookingId, tourTitle, review, canEdit }: { bookingId: string; tourTitle: string; review?: MyReview; canEdit: boolean }) {
  const router = useRouter();
  const commentId = useId();
  const [open, setOpen] = useState(false);
  const [rating, setRating] = useState(review?.rating ?? 0);
  const [comment, setComment] = useState(review?.comment ?? "");
  const [busy, setBusy] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [error, setError] = useState<string>();

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setError(undefined);
    try {
      await action();
      setOpen(false);
      router.refresh();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.userMessage : "That didn't work. Please try again.");
    } finally {
      setBusy(false);
    }
  }

  const body = { rating, comment: comment.trim() || null };
  const label = review ? (canEdit ? "Edit review" : "Your review") : "Write a review";

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (next) {
          setRating(review?.rating ?? 0);
          setComment(review?.comment ?? "");
          setConfirmDelete(false);
          setError(undefined);
        }
      }}
    >
      <DialogTrigger asChild>
        <Button variant={review ? "outline" : "default"}>{label}</Button>
      </DialogTrigger>
      <DialogContent>
        <div>
          <DialogTitle>{review ? "Your review" : "How was it?"}</DialogTitle>
          <DialogDescription className="mt-1">
            {tourTitle}. {review ? (canEdit ? "You can still change it." : "It can no longer be changed, but you can delete it.") : "Other travelers will see your first name and initial."}
          </DialogDescription>
        </div>
        <StarInput value={rating} onChange={setRating} disabled={busy || (Boolean(review) && !canEdit)} />
        <label htmlFor={commentId} className="flex flex-col gap-1.5 text-sm font-semibold">
          <span>Tell others about it <span className="font-normal text-muted-foreground">(optional)</span></span>
          <textarea
            id={commentId}
            rows={5}
            maxLength={MAX}
            disabled={busy || (Boolean(review) && !canEdit)}
            value={comment}
            onChange={(event) => setComment(event.target.value)}
            placeholder="What did you enjoy? Anything others should know?"
            className="rounded-control border border-line bg-surface px-3 py-2 font-normal"
          />
          <span className="self-end text-xs font-normal text-muted-foreground">{comment.length}/{MAX}</span>
        </label>
        <FormAlert message={error} />
        <div className="flex flex-wrap items-center gap-2">
          {(!review || canEdit) && (
            <Button pending={busy} disabled={rating < 1} onClick={() => run(() => (review ? reviewsApi.edit(bookingId, body) : reviewsApi.post(bookingId, body)))}>
              {busy ? "Saving…" : review ? "Save changes" : "Post review"}
            </Button>
          )}
          {review && (
            confirmDelete ? (
              <Button variant="urgent" disabled={busy} onClick={() => run(() => reviewsApi.remove(bookingId))}>Yes, delete it</Button>
            ) : (
              <Button variant="outline" disabled={busy} onClick={() => setConfirmDelete(true)}>Delete review</Button>
            )
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

