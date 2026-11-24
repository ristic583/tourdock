"use client";

import { formatRemaining, useNowSeconds } from "@/lib/use-now";

export { formatRemaining } from "@/lib/use-now";

/**
 * Hold expiry for a pending booking. Rendered in the browser so the time is in the traveler's own
 * time zone (the server runs in UTC), with a live countdown.
 */
export function HoldNotice({ expiresAt }: { expiresAt: string }) {
  const now = useNowSeconds();
  const expires = new Date(expiresAt);
  const remaining = now === null ? null : Math.floor(expires.getTime() / 1000) - now;

  let text: string;
  if (remaining === null) text = "Your seats are held for a few minutes. Complete payment to confirm this booking.";
  else if (remaining <= 0) text = "This hold has expired. Choose the departure again to book it.";
  else {
    const until = new Intl.DateTimeFormat("en", { hour: "numeric", minute: "2-digit" }).format(expires);
    text = `Your seats are held until ${until} (${formatRemaining(remaining)} left). Complete payment before then to confirm this booking.`;
  }

  return (
    <p
      role="status"
      className="mt-5 rounded-control bg-[var(--status-pending-bg)] px-3.5 py-2.5 text-sm font-medium text-[var(--status-pending-ink)]"
    >
      {text}
    </p>
  );
}

/** One-line countdown for a pending booking in a list, e.g. "Hold expires in 11:48". */
export function HoldCountdown({ expiresAt }: { expiresAt: string }) {
  const now = useNowSeconds();
  const remaining = now === null ? null : Math.floor(new Date(expiresAt).getTime() / 1000) - now;
  return (
    <p className="text-[13px] font-medium text-[var(--status-pending-ink)]">
      {remaining === null ? "Seats held while you pay" : remaining > 0 ? `Hold expires in ${formatRemaining(remaining)}` : "Hold expired, updating…"}
    </p>
  );
}
