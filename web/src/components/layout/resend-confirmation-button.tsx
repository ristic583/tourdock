"use client";

import { useState, useTransition } from "react";

import { resendEmailConfirmation } from "@/lib/auth/actions";

/** "Resend link", then a short confirmation or the error, in place. */
export function ResendConfirmationButton() {
  const [pending, start] = useTransition();
  const [state, setState] = useState<{ sent: true } | { error: string }>();

  if (state && "sent" in state) return <p className="text-sm font-semibold text-primary">New link sent. Check your inbox.</p>;
  return (
    <div className="flex items-center gap-3">
      {state && "error" in state && <p className="text-sm text-urgent-ink">{state.error}</p>}
      <button
        type="button"
        disabled={pending}
        onClick={() =>
          start(async () => {
            const result = await resendEmailConfirmation();
            setState(result.ok ? { sent: true } : { error: result.error.message });
          })
        }
        className="cursor-pointer rounded-control px-2 py-1 text-sm font-semibold text-primary hover:text-primary-hover disabled:opacity-50"
      >
        {pending ? "Sending…" : "Resend link"}
      </button>
    </div>
  );
}
