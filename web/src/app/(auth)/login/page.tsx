import type { Metadata } from "next";
import { Suspense } from "react";

import { GoogleSignIn } from "@/components/auth/google-sign-in";
import { LoginForm } from "@/components/auth/login-form";
import { googleClientId } from "@/lib/auth/providers";
import { safeNextPath } from "@/lib/auth/redirect";

import { AuthCard } from "../auth-card";

export const metadata: Metadata = { title: "Sign in", robots: { index: false } };

export default function LoginPage({ searchParams }: PageProps<"/login">) {
  return (
    <AuthCard title="Welcome back" subtitle="Sign in to book tours and manage your trips.">
      <Suspense fallback={<LoginForm />}>
        <LoginFormWithNext searchParams={searchParams} />
      </Suspense>
    </AuthCard>
  );
}

async function LoginFormWithNext({ searchParams }: Pick<PageProps<"/login">, "searchParams">) {
  const { next, error } = await searchParams;
  const target = safeNextPath(typeof next === "string" ? next : undefined, "");
  return (
    <>
      <GoogleSignIn enabled={Boolean(await googleClientId())} next={target || undefined} failure={typeof error === "string" ? error : undefined} />
      <LoginForm next={target || undefined} />
    </>
  );
}
