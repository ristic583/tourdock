"use client";

import { useRouter } from "next/navigation";
import { useOptimistic, useTransition } from "react";

import { CURRENCY_COOKIE, DISPLAY_CURRENCIES, type DisplayCurrency } from "@/lib/currency";
import { cn } from "@/lib/utils";

const ONE_YEAR = 60 * 60 * 24 * 365;

/**
 * Display currency for prices. Stored in a plain cookie (not sensitive), then the page re-renders on the server
 * so every price on it uses the new currency.
 */
export function CurrencyPicker({ value, disabled }: { value: DisplayCurrency; disabled?: boolean }) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  // Shows the new choice right away while the server re-renders the page with it.
  const [shown, setShown] = useOptimistic(value);

  return (
    <label className="relative inline-flex items-center">
      <span className="sr-only">Show prices in</span>
      <select
        value={shown}
        disabled={disabled || pending}
        onChange={(event) => {
          const next = event.target.value as DisplayCurrency;
          document.cookie = `${CURRENCY_COOKIE}=${next}; path=/; max-age=${ONE_YEAR}; samesite=lax`;
          startTransition(() => {
            setShown(next);
            router.refresh();
          });
        }}
        className={cn(
          "h-10 cursor-pointer appearance-none rounded-full border border-line bg-surface pr-3 pl-3 text-sm font-semibold",
          "hover:bg-surface-2 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary disabled:cursor-default disabled:opacity-60",
        )}
      >
        {DISPLAY_CURRENCIES.map((code) => (
          <option key={code} value={code}>{code}</option>
        ))}
      </select>
    </label>
  );
}
