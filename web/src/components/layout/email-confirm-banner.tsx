import { MailCheck } from "lucide-react";

import { getCurrentUser } from "@/lib/auth/dal";

import { ResendConfirmationButton } from "./resend-confirmation-button";

/** Under the header for signed-in users who haven't confirmed their email yet: booking waits for it. */
export async function EmailConfirmBanner() {
  const user = await getCurrentUser().catch(() => null);
  if (!user || user.emailConfirmed) return null;
  return (
    <div role="status" className="border-b border-line bg-primary-soft">
      <div className="mx-auto flex max-w-page flex-wrap items-center gap-x-4 gap-y-2 px-4 py-2.5 text-sm sm:px-6">
        <MailCheck className="size-4 shrink-0 text-primary" aria-hidden />
        <p className="min-w-0 flex-1">
          <span className="font-semibold">Confirm your email to book tours.</span>{" "}
          <span className="text-muted-foreground">We sent a link to <span className="font-medium text-ink">{user.email}</span>.</span>
        </p>
        <ResendConfirmationButton />
      </div>
    </div>
  );
}
