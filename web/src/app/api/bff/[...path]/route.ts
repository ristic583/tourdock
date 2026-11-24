import { NextResponse, type NextRequest } from "next/server";

import type { ProblemDetails } from "@/lib/api/problem";
import { ensureAccessToken } from "@/lib/auth/session";
import { isSameOrigin, traceparent, upstreamPath } from "@/lib/bff";
import { clientIp } from "@/lib/client-ip";
import { apiBaseUrl } from "@/lib/env";

/**
 * Backend-for-frontend pass-through for client-side data fetching (TanStack Query).
 * `/api/bff/api/v1/...` → `${API_BASE_URL}/api/v1/...` with the access token from the httpOnly cookie
 * added server-side. Auth endpoints are not exposed here: their responses contain tokens,
 * which must never reach the browser (sign-in goes through Server Actions instead).
 */

const FORWARDED_REQUEST_HEADERS = ["accept", "accept-language", "content-type", "idempotency-key"];
const FORWARDED_RESPONSE_HEADERS = ["content-type", "idempotent-replayed", "location", "retry-after"];

function problem(status: number, title: string, code?: string) {
  const body: ProblemDetails = { status, title, ...(code ? { code } : {}) };
  return NextResponse.json(body, { status, headers: { "content-type": "application/problem+json" } });
}

async function handle(request: NextRequest, context: { params: Promise<{ path: string[] }> }) {
  const path = upstreamPath((await context.params).path);
  if (!path) return problem(404, "Not found");

  const method = request.method.toUpperCase();
  const isRead = method === "GET" || method === "HEAD";
  if (!isRead && !isSameOrigin(request.headers)) return problem(403, "Cross-site request blocked", "Bff.CrossSite");

  const body = isRead ? undefined : await request.arrayBuffer();
  const trace = traceparent(request.headers.get("traceparent"));
  const url = `${apiBaseUrl()}/${path}${request.nextUrl.search}`;

  const send = (token: string | null) => {
    const headers = new Headers();
    for (const name of FORWARDED_REQUEST_HEADERS) {
      const value = request.headers.get(name);
      if (value) headers.set(name, value);
    }
    if (token) headers.set("authorization", `Bearer ${token}`);
    const ip = clientIp(request.headers);
    if (ip) headers.set("x-forwarded-for", ip);
    headers.set("traceparent", trace);
    return fetch(url, { method, headers, body, cache: "no-store", redirect: "manual" });
  };

  let upstream: Response;
  try {
    const token = await ensureAccessToken();
    upstream = await send(token);
    // The token can be rejected before its expiry (for example after a server-side revocation): refresh once.
    if (upstream.status === 401 && token) {
      const fresh = await ensureAccessToken({ force: true });
      if (fresh && fresh !== token) upstream = await send(fresh);
    }
  } catch {
    return problem(503, "The API is unavailable", "Network.Unavailable");
  }

  const headers = new Headers();
  for (const name of FORWARDED_RESPONSE_HEADERS) {
    const value = upstream.headers.get(name);
    if (value) headers.set(name, value);
  }
  headers.set("cache-control", "no-store");
  return new NextResponse(upstream.body, { status: upstream.status, headers });
}

export { handle as GET, handle as POST, handle as PUT, handle as PATCH, handle as DELETE };
