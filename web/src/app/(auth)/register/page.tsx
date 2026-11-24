import type { Metadata } from "next";
import { Suspense } from "react";

import { GoogleSignIn } from "@/components/auth/google-sign-in";
import { RegisterForm } from "@/components/auth/register-form";
import { googleClientId } from "@/lib/auth/providers";
import { safeNextPath } from "@/lib/auth/redirect";

import { AuthCard } from "../auth-card";

export const metadata: Metadata = { title: "Create an account", robots: { index: false } };

export default function RegisterPage({ searchParams }: PageProps<"/register">) {
  return (
    <AuthCard title="Create your account" subtitle="Book a seat on a shared tour, or the whole trip for your group.">
      <Suspense fallback={<RegisterForm />}>
        <RegisterFormWithNext searchParams={searchParams} />
      </Suspense>
    </AuthCard>
  );
}

async function RegisterFormWithNext({ searchParams }: Pick<PageProps<"/register">, "searchParams">) {
  const { next } = await searchParams;
  const target = safeNextPath(typeof next === "string" ? next : undefined, "");
  return (
    <>
      <GoogleSignIn enabled={Boolean(await googleClientId())} next={target || undefined} />
      <RegisterForm next={target || undefined} />
    </>
  );
}
