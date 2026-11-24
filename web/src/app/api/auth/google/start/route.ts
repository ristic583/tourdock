import { NextResponse, type NextRequest } from "next/server";

import { writeCookies } from "@/lib/auth/cookies";
import { attemptCookie, authorizeUrl, codeChallenge, failurePath, GOOGLE_CALLBACK, randomToken } from "@/lib/auth/google";
import { googleClientId } from "@/lib/auth/providers";
import { safeNextPath } from "@/lib/auth/redirect";
import { siteOrigin } from "@/lib/env";

/** Sends the visitor to Google with a fresh `state` and PKCE challenge (ADR 0029); both are checked on the way back. */
export async function GET(request: NextRequest) {
  const origin = siteOrigin(request.headers);
  const next = safeNextPath(request.nextUrl.searchParams.get("next"));
  const clientId = await googleClientId();
  if (!clientId) return NextResponse.redirect(new URL(failurePath("google_unavailable", next), origin));

  const state = randomToken();
  const verifier = randomToken();
  const response = NextResponse.redirect(authorizeUrl(clientId, origin + GOOGLE_CALLBACK, state, await codeChallenge(verifier)));
  writeCookies(response.cookies, [attemptCookie({ state, verifier, next })]);
  return response;
}
