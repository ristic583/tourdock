import Link from "next/link";
import { Suspense } from "react";

import { Logo } from "@/components/brand/logo";
import { getCurrentUser } from "@/lib/auth/dal";

import { getDisplayCurrency } from "@/lib/display-currency";

import { AccountMenu } from "./account-menu";
import { CurrencyPicker } from "./currency-picker";
import { MainNav } from "./main-nav";
import { SignInLinks } from "./sign-in-links";
import { ThemeToggle } from "./theme";

export function SiteHeader() {
  return (
    <header className="sticky top-0 z-40 border-b border-line bg-bg/95 backdrop-blur supports-[backdrop-filter]:bg-bg/85">
      <div className="mx-auto flex max-w-page items-center gap-2 px-4 py-3 sm:gap-6 sm:px-6 sm:py-3.5">
        <Link href="/" aria-label="TourDock home" className="text-ink">
          <Logo />
        </Link>
        <MainNav />
        <div className="flex-1" />
        <Suspense fallback={<CurrencyPicker value="EUR" disabled />}>
          <HeaderCurrency />
        </Suspense>
        <ThemeToggle />
        {/* The session is read per request; everything else in the header is part of the static shell. */}
        <Suspense fallback={<div className="h-10 w-28 animate-pulse rounded-full bg-surface-2" aria-hidden />}>
          <HeaderAccount />
        </Suspense>
      </div>
    </header>
  );
}

async function HeaderCurrency() {
  return <CurrencyPicker value={await getDisplayCurrency()} />;
}

async function HeaderAccount() {
  // If the API is down the header still renders, as signed out, instead of failing the whole page.
  const user = await getCurrentUser().catch(() => null);
  return user ? <AccountMenu displayName={user.displayName} email={user.email} isAdmin={user.roles.includes("Admin")} /> : <SignInLinks />;
}
