import "server-only";

import { cookies } from "next/headers";
import { cache } from "react";

import type { ExchangeRates } from "@/lib/api/contracts";
import { unwrap } from "@/lib/api/client";
import { serverApi } from "@/lib/api/server";
import { BASE_CURRENCY, CURRENCY_COOKIE, displayRate, parseCurrency, type DisplayCurrency, type DisplayRate } from "@/lib/currency";

/** The traveler's chosen display currency (EUR when unset). Reading the cookie makes the caller dynamic. */
export async function getDisplayCurrency(): Promise<DisplayCurrency> {
  return parseCurrency((await cookies()).get(CURRENCY_COOKIE)?.value);
}

/**
 * Rates from our API, once per render. The API caches them for hours and falls back to the last good rates, so this is
 * cheap; if even the API is unreachable, prices simply show in EUR only.
 */
const getExchangeRates = cache(async (): Promise<ExchangeRates | null> =>
  unwrap<ExchangeRates>(serverApi().GET("/api/v1/exchange-rates")).catch(() => null),
);

/** The rate for approximate prices, or null for EUR only. Skips the rates call when EUR is chosen. */
export const getDisplayRate = cache(async (): Promise<DisplayRate | null> => {
  const currency = await getDisplayCurrency();
  return currency === BASE_CURRENCY ? null : displayRate(currency, await getExchangeRates());
});
