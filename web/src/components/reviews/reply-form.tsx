"use client";

import { useRouter } from "next/navigation";
import { useId, useState } from "react";

import { FormAlert } from "@/components/feedback/form-alert";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/lib/api/problem";
import { reviewsApi } from "@/lib/api/reviews-client";

const MAX = 1000;

/** The agency's one public reply to a review: write it, change it or take it down (managers only). */
export function ReplyForm({ agencyId, reviewId, reply }: { agencyId: string; reviewId: string; reply: string | null }) {
  const router = useRouter();
  const textId = useId();
  const [editing, setEditing] = useState(false);
  const [text, setText] = useState(reply ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setError(undefined);
    try {
      await action();
      setEditing(false);
      router.refresh();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.userMessage : "That didn't work. Please try again.");
    } finally {
      setBusy(false);
    }
  }

  if (!editing) {
    return (
      <div className="flex flex-wrap gap-2">
        <Button size="sm" variant="outline" onClick={() => { setText(reply ?? ""); setEditing(true); }}>{reply ? "Edit reply" : "Reply publicly"}</Button>
        {reply && <Button size="sm" variant="ghost" disabled={busy} onClick={() => run(() => reviewsApi.removeReply(agencyId, reviewId))}>Remove reply</Button>}
        <FormAlert message={error} />
      </div>
    );
  }

  return (
    <div className="flex max-w-[640px] flex-col gap-2">
      <label htmlFor={textId} className="text-sm font-semibold">Your public reply</label>
      <textarea
        id={textId}
        rows={4}
        maxLength={MAX}
        value={text}
        onChange={(event) => setText(event.target.value)}
        className="rounded-control border border-line bg-surface px-3 py-2"
        placeholder="Thank the traveler, or explain what happened. Everyone can read this."
      />
      <span className="self-end text-xs text-muted-foreground">{text.length}/{MAX}</span>
      <FormAlert message={error} />
      <div className="flex gap-2">
        <Button size="sm" pending={busy} disabled={!text.trim()} onClick={() => run(() => reviewsApi.reply(agencyId, reviewId, text.trim()))}>{busy ? "Saving…" : "Publish reply"}</Button>
        <Button size="sm" variant="ghost" disabled={busy} onClick={() => setEditing(false)}>Cancel</Button>
      </div>
    </div>
  );
}
