import "server-only";

import { cookies, headers } from "next/headers";

import { clientIp } from "@/lib/client-ip";

import { clearedCookies, readTokenCookies, tokenCookies, writeCookies } from "./cookies";
import { isExpiring } from "./jwt";
import { refreshSession } from "./refresh";

/**
 * Returns an access token for an API call made from a Server Action or Route Handler,
 * refreshing (and rewriting the cookies) when it is about to expire. Server Components can't
 * write cookies, so for page renders the proxy does the refresh before rendering starts.
 */
export async function ensureAccessToken({ force = false } = {}): Promise<string | null> {
  const store = await cookies();
  const { accessToken, refreshToken } = readTokenCookies(store);

  if (accessToken && !force && !isExpiring(accessToken)) return accessToken;
  if (!refreshToken) return null;

  const outcome = await refreshSession(refreshToken, { clientIp: clientIp(await headers()) });
  switch (outcome.kind) {
    case "ok":
      writeCookies(store, tokenCookies(outcome.tokens));
      return outcome.tokens.accessToken;
    case "invalid":
      writeCookies(store, clearedCookies());
      return null;
    case "unavailable":
      return accessToken ?? null;
  }
}

export async function readRefreshToken(): Promise<string | undefined> {
  return readTokenCookies(await cookies()).refreshToken;
}

export async function readAccessToken(): Promise<string | undefined> {
  return readTokenCookies(await cookies()).accessToken;
}
