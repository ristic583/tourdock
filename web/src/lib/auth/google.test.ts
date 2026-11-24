import { describe, expect, it } from "vitest";

import { attemptCookie, authorizeUrl, codeChallenge, failureFor, failurePath, GOOGLE_ROUTES, randomToken, readAttempt } from "./google";

describe("Google sign-in", () => {
  it("makes the PKCE challenge exactly as RFC 7636 does", async () => {
    expect(await codeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")).toBe("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
  });

  it("uses fresh 43-character tokens that fit a URL", () => {
    const [a, b] = [randomToken(), randomToken()];
    expect(a).toMatch(/^[A-Za-z0-9_-]{43}$/);
    expect(a).not.toBe(b);
  });

  it("asks Google for an authorization code with PKCE, state and only name and email", () => {
    const url = new URL(authorizeUrl("client-1", "https://tours.example.com/api/auth/google/callback", "s1", "c1"));
    expect(url.origin + url.pathname).toBe("https://accounts.google.com/o/oauth2/v2/auth");
    expect(Object.fromEntries(url.searchParams)).toMatchObject({
      client_id: "client-1",
      redirect_uri: "https://tours.example.com/api/auth/google/callback",
      response_type: "code",
      scope: "openid email profile",
      state: "s1",
      code_challenge: "c1",
      code_challenge_method: "S256",
    });
  });

  it("keeps the attempt in an httpOnly cookie only the Google routes receive, and clears it", () => {
    const cookie = attemptCookie({ state: "s1", verifier: "v1", next: "/tours" });
    expect(cookie.options).toMatchObject({ httpOnly: true, sameSite: "lax", path: GOOGLE_ROUTES });
    expect(readAttempt(cookie.value)).toEqual({ state: "s1", verifier: "v1", next: "/tours" });
    expect(attemptCookie(null).options.expires.getTime()).toBe(0);
  });

  it("never trusts a tampered cookie or sends the visitor off-site", () => {
    expect(readAttempt(undefined)).toBeUndefined();
    expect(readAttempt("not-json")).toBeUndefined();
    const offSite = attemptCookie({ state: "s", verifier: "v", next: "//evil.example" }).value;
    expect(readAttempt(offSite)?.next).toBe("/");
  });

  it("explains failures on the sign-in page and keeps where the visitor was going", () => {
    expect(failurePath("google_cancelled", "/bookings")).toBe("/login?error=google_cancelled&next=%2Fbookings");
    expect(failurePath("google", "/")).toBe("/login?error=google");
    expect(failureFor("Auth.ExternalEmailNotVerified")).toBe("google_unverified");
    expect(failureFor("Auth.GoogleUnavailable")).toBe("google_unavailable");
    expect(failureFor(undefined)).toBe("google");
  });
});
