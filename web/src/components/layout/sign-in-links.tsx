"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

import { Button } from "@/components/ui/button";
import { loginPath } from "@/lib/auth/redirect";

/** Sign in / Sign up buttons that bring the visitor back to the page they were on. */
export function SignInLinks() {
  const pathname = usePathname();
  const back = pathname.startsWith("/login") || pathname.startsWith("/register") ? undefined : pathname;
  const registerHref = back && back !== "/" ? `/register?next=${encodeURIComponent(back)}` : "/register";

  return (
    <div className="flex items-center gap-2">
      <Button asChild variant="ghost" size="sm" className="min-h-10">
        <Link href={loginPath(back)}>Sign in</Link>
      </Button>
      <Button asChild size="sm" className="hidden min-h-10 sm:inline-flex">
        <Link href={registerHref}>Sign up</Link>
      </Button>
    </div>
  );
}
