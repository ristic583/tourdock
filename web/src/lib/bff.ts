/** Request checks for the BFF pass-through route (kept separate so they're easy to unit test). */

const BLOCKED = [/^api\/v1\/auth(\/|$)/i, /^api\/v1\/webhooks(\/|$)/i];

/**
 * Maps `/api/bff/<segments>` to the API path, or null when it must not be forwarded:
 * anything outside `api/v1/`, path tricks, and the auth/webhook endpoints (their responses carry tokens
 * or they're only for the payment provider).
 */
export function upstreamPath(segments: string[]): string | null {
  if (segments.some((s) => s === "" || s === "." || s === ".." || s.includes("/") || s.includes("\\"))) return null;
  const path = segments.join("/");
  if (!path.startsWith("api/v1/") || BLOCKED.some((re) => re.test(path))) return null;
  return segments.map(encodeURIComponent).join("/");
}

/** SameSite=Lax already stops cross-site POSTs from carrying the cookies; this is defense in depth. */
export function isSameOrigin(headers: Headers): boolean {
  const fetchSite = headers.get("sec-fetch-site");
  if (fetchSite) return fetchSite === "same-origin";
  const origin = headers.get("origin");
  if (!origin) return false;
  try {
    return new URL(origin).host === headers.get("host");
  } catch {
    return false;
  }
}

const TRACEPARENT = /^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$/;

function randomHex(bytes: number): string {
  const values = crypto.getRandomValues(new Uint8Array(bytes));
  return Array.from(values, (b) => b.toString(16).padStart(2, "0")).join("");
}

/**
 * W3C trace context for the API call: the incoming one when it's well formed, otherwise a new one. The API logs every
 * line of the request with this trace id and returns it as `traceId` in errors, so one id finds all of it.
 */
export function traceparent(incoming: string | null): string {
  if (incoming && TRACEPARENT.test(incoming) && !incoming.startsWith("00-00000000000000000000000000000000")) return incoming;
  return `00-${randomHex(16)}-${randomHex(8)}-01`;
}
