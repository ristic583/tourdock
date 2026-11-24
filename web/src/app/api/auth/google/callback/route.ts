import { NextResponse, type NextRequest } from "next/server";

import type { AuthResponse } from "@/lib/api/contracts";
import { unwrap } from "@/lib/api/client";
import { ApiError } from "@/lib/api/problem";
import { serverApi } from "@/lib/api/server";
import { tokenCookies, writeCookies } from "@/lib/auth/cookies";
import { attemptCookie, failureFor, failurePath, GOOGLE_CALLBACK, GOOGLE_COOKIE, readAttempt, type GoogleFailure } from "@/lib/auth/google";
import { forwardedForHeader } from "@/lib/client-ip";
import { siteOrigin } from "@/lib/env";

/**
 * Google sends the visitor back here with a code. The `state` must match the one this browser was sent off with (so
 * nobody can slip their own code into someone else's sign-in); then the API redeems the code with the PKCE verifier and
 * answers with the same tokens a password sign-in gets. The attempt cookie is cleared either way.
 */
export async function GET(request: NextRequest) {
  const origin = siteOrigin(request.headers);
  const params = request.nextUrl.searchParams;
  const attempt = readAttempt(request.cookies.get(GOOGLE_COOKIE)?.value);
  const next = attempt?.next ?? "/";
  const fail = (reason: GoogleFailure) => {
    const response = NextResponse.redirect(new URL(failurePath(reason, next), origin));
    writeCookies(response.cookies, [attemptCookie(null)]);
    return response;
  };

  if (params.get("error")) return fail("google_cancelled");
  const code = params.get("code");
  if (!attempt || !code || params.get("state") !== attempt.state) return fail("google");

  try {
    const result = await unwrap<AuthResponse>(
      serverApi(null, forwardedForHeader(request.headers)).POST("/api/v1/auth/google", {
        body: { code, codeVerifier: attempt.verifier, redirectUri: origin + GOOGLE_CALLBACK },
      }),
    );
    const response = NextResponse.redirect(new URL(next, origin));
    writeCookies(response.cookies, [...tokenCookies(result.tokens), attemptCookie(null)]);
    return response;
  } catch (error) {
    return fail(failureFor(error instanceof ApiError ? error.code : undefined));
  }
}
