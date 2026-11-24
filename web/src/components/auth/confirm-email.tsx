"use client";

import { CheckCircle2, XCircle } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";

import { Button } from "@/components/ui/button";
import { confirmEmail, confirmEmailChange } from "@/lib/auth/actions";

type State = { status: "checking" } | { status: "confirmed" } | { status: "failed"; message: string };

/**
 * Confirms once, after the page has loaded in a browser. Doing it from the browser rather than while rendering keeps
 * prefetchers and previews from spending the link, and lets the header drop the "confirm your email" banner after.
 */
export function ConfirmEmail({ userId, token, change = false }: { userId: string; token: string; change?: boolean }) {
  const router = useRouter();
  const started = useRef(false);
  const [state, setState] = useState<State>(() =>
    userId && token ? { status: "checking" } : { status: "failed", message: "This link is incomplete. Open it from the email again." },
  );

  useEffect(() => {
    if (started.current || !userId || !token) return;
    started.current = true;
    (change ? confirmEmailChange : confirmEmail)(userId, token).then((result) => {
      setState(result.ok ? { status: "confirmed" } : { status: "failed", message: result.error.message });
      if (result.ok) router.refresh();
    });
  }, [userId, token, change, router]);

  if (state.status === "checking") return <p className="text-muted-foreground" aria-live="polite">Checking your link…</p>;

  if (state.status === "confirmed") {
    return (
      <div className="flex flex-col gap-5" aria-live="polite">
        <p className="flex items-center gap-2.5 font-semibold text-primary"><CheckCircle2 className="size-5" aria-hidden /> {change ? "Your new email is in use." : "Your email is confirmed."}</p>
        <p className="text-muted-foreground">{change ? "Sign in with the new address from now on. Booking emails go there too." : "Sign in to book tours and list your own agency."}</p>
        {change ? <Button asChild><Link href="/account">Back to your account</Link></Button> : <Button asChild><Link href="/login?next=/tours">Sign in</Link></Button>}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-5" aria-live="polite">
      <p className="flex items-start gap-2.5 text-urgent-ink"><XCircle className="mt-0.5 size-5 shrink-0" aria-hidden /> {state.message}</p>
      <p className="text-muted-foreground">{change ? "Signed in? Change your email again from your account page to get a new link." : <>Sign in with your email and password, and we&rsquo;ll send you a new link.</>}</p>
      <Button asChild variant="outline"><Link href="/login?next=/">Sign in</Link></Button>
    </div>
  );
}
