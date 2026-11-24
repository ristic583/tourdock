import type { AuthTokens } from "@/lib/api/contracts";
import { cookiesSecure } from "@/lib/env";

/**
 * The tokens live only in httpOnly cookies. Browser JavaScript can't read them, so an XSS bug
 * can't steal a session; the BFF reads them server-side and calls the API with a Bearer header.
 */
export const ACCESS_COOKIE = "td_at";
export const REFRESH_COOKIE = "td_rt";

export type CookieToSet = {
  name: string;
  value: string;
  options: {
    httpOnly: true;
    secure: boolean;
    sameSite: "lax";
    path: string;
    expires: Date;
  };
};

function options(expires: Date) {
  return { httpOnly: true as const, secure: cookiesSecure(), sameSite: "lax" as const, path: "/", expires };
}

/*
 * Cookie values are base64url-encoded by us. The refresh token is standard base64 (`+`, `/`, `=`), and
 * different Next.js cookie APIs escape such characters differently on write and read, which mangles the
 * token now and then. Encoding it into a cookie-safe alphabet ourselves makes the round trip exact.
 */
export function encodeCookieValue(token: string): string {
  const bytes = new TextEncoder().encode(token);
  let binary = "";
  for (const b of bytes) binary += String.fromCharCode(b);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

export function decodeCookieValue(value: string | undefined): string | undefined {
  if (!value || !/^[A-Za-z0-9_-]+$/.test(value)) return undefined;
  try {
    const base64 = value.replace(/-/g, "+").replace(/_/g, "/");
    const binary = atob(base64 + "=".repeat((4 - (base64.length % 4)) % 4));
    return new TextDecoder().decode(Uint8Array.from(binary, (c) => c.charCodeAt(0)));
  } catch {
    return undefined;
  }
}

type ReadableCookies = { get(name: string): { value: string } | undefined };

/** Reads both tokens from a cookie store (`cookies()` or `NextRequest.cookies`). */
export function readTokenCookies(store: ReadableCookies): { accessToken?: string; refreshToken?: string } {
  return {
    accessToken: decodeCookieValue(store.get(ACCESS_COOKIE)?.value),
    refreshToken: decodeCookieValue(store.get(REFRESH_COOKIE)?.value),
  };
}

export function tokenCookies(tokens: AuthTokens): CookieToSet[] {
  return [
    {
      name: ACCESS_COOKIE,
      value: encodeCookieValue(tokens.accessToken),
      options: options(new Date(tokens.accessTokenExpiresAt)),
    },
    {
      name: REFRESH_COOKIE,
      value: encodeCookieValue(tokens.refreshToken),
      options: options(new Date(tokens.refreshTokenExpiresAt)),
    },
  ];
}

export function clearedCookies(): CookieToSet[] {
  return [ACCESS_COOKIE, REFRESH_COOKIE].map((name) => ({ name, value: "", options: options(new Date(0)) }));
}

/** Minimal cookie store shape shared by `cookies()` and `NextResponse.cookies`. */
type WritableCookies = { set(name: string, value: string, options: CookieToSet["options"]): unknown };

export function writeCookies(store: WritableCookies, cookies: CookieToSet[]): void {
  for (const c of cookies) store.set(c.name, c.value, c.options);
}
