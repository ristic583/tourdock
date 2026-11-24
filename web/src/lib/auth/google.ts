import { cookiesSecure } from "@/lib/env";

import { decodeCookieValue, encodeCookieValue, type CookieToSet } from "./cookies";
import { safeNextPath } from "./redirect";

/**
 * "Continue with Google" (ADR 0029). The web app only walks the browser to Google and back: it makes the `state` and
 * the PKCE verifier, keeps them in a short-lived cookie scoped to these routes, and hands the returned code to the API,
 * which redeems it with the client secret. The client id comes from the API, so it is configured in one place.
 */
export const GOOGLE_COOKIE = "td_google";
export const GOOGLE_ROUTES = "/api/auth/google";
export const GOOGLE_CALLBACK = `${GOOGLE_ROUTES}/callback`;
const AUTHORIZE_URL = "https://accounts.google.com/o/oauth2/v2/auth";
const ATTEMPT_MINUTES = 10;

export type GoogleAttempt = { state: string; verifier: string; next: string };

/** Why a Google sign-in ended on the sign-in page; the page explains each one. */
export type GoogleFailure = "google" | "google_cancelled" | "google_unverified" | "google_unavailable";

export const GOOGLE_FAILURES: Record<GoogleFailure, string> = {
  google: "Signing in with Google didn't work. Please try again.",
  google_cancelled: "Google sign-in was cancelled.",
  google_unverified: "Your Google account's email address isn't verified, so it can't be used to sign in here.",
  google_unavailable: "Signing in with Google isn't available right now. Use your email and password.",
};

function base64url(bytes: Uint8Array): string {
  let binary = "";
  for (const b of bytes) binary += String.fromCharCode(b);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

/** 32 random bytes, base64url: 43 characters, also a valid PKCE verifier (RFC 7636). */
export function randomToken(): string {
  return base64url(crypto.getRandomValues(new Uint8Array(32)));
}

export async function codeChallenge(verifier: string): Promise<string> {
  return base64url(new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier))));
}

export function authorizeUrl(clientId: string, redirectUri: string, state: string, challenge: string): string {
  const params = new URLSearchParams({
    client_id: clientId,
    redirect_uri: redirectUri,
    response_type: "code",
    scope: "openid email profile",
    state,
    code_challenge: challenge,
    code_challenge_method: "S256",
    prompt: "select_account",
  });
  return `${AUTHORIZE_URL}?${params}`;
}

export function attemptCookie(attempt: GoogleAttempt | null): CookieToSet {
  const expires = attempt ? new Date(Date.now() + ATTEMPT_MINUTES * 60_000) : new Date(0);
  return {
    name: GOOGLE_COOKIE,
    value: attempt ? encodeCookieValue(JSON.stringify(attempt)) : "",
    options: { httpOnly: true, secure: cookiesSecure(), sameSite: "lax", path: GOOGLE_ROUTES, expires },
  };
}

export function readAttempt(value: string | undefined): GoogleAttempt | undefined {
  try {
    const parsed: unknown = JSON.parse(decodeCookieValue(value) ?? "null");
    if (!parsed || typeof parsed !== "object") return undefined;
    const { state, verifier, next } = parsed as Record<string, unknown>;
    if (typeof state !== "string" || typeof verifier !== "string" || typeof next !== "string") return undefined;
    return { state, verifier, next: safeNextPath(next) };
  } catch {
    return undefined;
  }
}

/** The sign-in page, saying what went wrong and keeping where the visitor was going. */
export function failurePath(reason: GoogleFailure, next: string): string {
  const params = new URLSearchParams({ error: reason });
  if (next !== "/") params.set("next", next);
  return `/login?${params}`;
}

/** Maps the API's answer to what the sign-in page explains. */
export function failureFor(code: string | undefined): GoogleFailure {
  if (code === "Auth.ExternalEmailNotVerified") return "google_unverified";
  if (code === "Auth.GoogleUnavailable") return "google_unavailable";
  return "google";
}
