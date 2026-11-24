import { NextResponse, type NextRequest } from "next/server";

import { clearedCookies, readTokenCookies, tokenCookies, writeCookies, type CookieToSet } from "@/lib/auth/cookies";
import { isExpiring } from "@/lib/auth/jwt";
import { loginPath } from "@/lib/auth/redirect";
import { refreshSession } from "@/lib/auth/refresh";
import { isProtectedPath } from "@/lib/auth/routes";
import { clientIp } from "@/lib/client-ip";

/**
 * Runs before every page, RSC request, Server Action and BFF call:
 *  1. refreshes the access token when it is missing or about to expire (serialized, see refresh.ts),
 *     writing the new cookies to the response *and* to the forwarded request so the render sees them;
 *  2. sends anonymous visitors of protected pages to the login page.
 * Pages and Server Actions still check the user themselves; this is a convenience, not the guard.
 */
export async function proxy(request: NextRequest) {
  const { accessToken, refreshToken } = readTokenCookies(request.cookies);

  let signedIn = Boolean(refreshToken);
  let cookiesToSet: CookieToSet[] = [];

  if (refreshToken && isExpiring(accessToken)) {
    const outcome = await refreshSession(refreshToken, { clientIp: clientIp(request.headers) });
    if (outcome.kind === "ok") {
      cookiesToSet = tokenCookies(outcome.tokens);
    } else if (outcome.kind === "invalid") {
      cookiesToSet = clearedCookies();
      signedIn = false;
    }
  }

  const { pathname, search } = request.nextUrl;
  if (!signedIn && isProtectedPath(pathname)) {
    const response = NextResponse.redirect(new URL(loginPath(pathname + search), request.url));
    writeCookies(response.cookies, cookiesToSet);
    return response;
  }

  if (cookiesToSet.length === 0) return NextResponse.next();

  for (const cookie of cookiesToSet) {
    if (cookie.value) request.cookies.set(cookie.name, cookie.value);
    else request.cookies.delete(cookie.name);
  }
  const response = NextResponse.next({ request: { headers: request.headers } });
  writeCookies(response.cookies, cookiesToSet);
  return response;
}

export const config = {
  matcher: ["/((?!_next/static|_next/image|favicon.ico|icon.svg|robots.txt|api/health).*)"],
};
