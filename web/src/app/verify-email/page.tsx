import type { Metadata } from "next";
import { Suspense } from "react";

import { ConfirmEmail } from "@/components/auth/confirm-email";

import { AuthCard } from "../(auth)/auth-card";

export const metadata: Metadata = { title: "Confirm your email", robots: { index: false } };

/** Landing page of the link in the sign-up email: confirms the address, then sends the visitor on. */
export default function VerifyEmailPage({ searchParams }: PageProps<"/verify-email">) {
  return (
    <AuthCard title="Confirm your email" subtitle="One moment while we check your link.">
      <Suspense fallback={<p className="text-muted-foreground">Checking your link…</p>}>
        <ConfirmFromLink searchParams={searchParams} />
      </Suspense>
    </AuthCard>
  );
}

async function ConfirmFromLink({ searchParams }: Pick<PageProps<"/verify-email">, "searchParams">) {
  const { user, token, change } = await searchParams;
  return <ConfirmEmail userId={typeof user === "string" ? user : ""} token={typeof token === "string" ? token : ""} change={change === "1"} />;
}
