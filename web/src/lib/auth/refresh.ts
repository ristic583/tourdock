import type { AuthTokens } from "@/lib/api/contracts";
import { apiBaseUrl } from "@/lib/env";

/**
 * Refresh-token rotation, serialized per refresh token.
 *
 * The API rotates the refresh token on every `POST /auth/refresh`, and presenting an already-used
 * token revokes the whole session (reuse detection). A page load fires several requests at once
 * (the document, RSC fetches, Server Actions), and each would try to refresh with the same cookie.
 * So:
 *  - concurrent refreshes with the same token share one in-flight API call (single flight), and
 *  - for a short grace period, a request that still carries the old token gets the new tokens
 *    instead of calling the API again (its cookies were updated by a response it hasn't seen yet).
 *
 * The state is per Node.js process, which matches one `web` container. Running several replicas
 * would need sticky sessions or a shared store (for example Redis) for the same guarantee.
 */

export type RefreshOutcome =
  | { kind: "ok"; tokens: AuthTokens }
  /** The API rejected the token: the session is over, clear the cookies. */
  | { kind: "invalid" }
  /** The API couldn't be reached or failed: keep the cookies and try again later. */
  | { kind: "unavailable" };

export const REFRESH_GRACE_MS = 30_000;

type RefreshState = {
  inflight: Map<string, Promise<RefreshOutcome>>;
  recent: Map<string, { tokens: AuthTokens; at: number }>;
};

// Stored on globalThis so the proxy, route handlers and Server Actions (separate bundles) share it.
const globalKey = Symbol.for("tourdock.auth.refresh");

function state(): RefreshState {
  const g = globalThis as typeof globalThis & { [globalKey]?: RefreshState };
  g[globalKey] ??= { inflight: new Map(), recent: new Map() };
  return g[globalKey];
}

export type RefreshDeps = {
  fetch?: typeof globalThis.fetch;
  baseUrl?: string;
  now?: () => number;
  /** The visitor's IP, passed on as X-Forwarded-For (see client-ip.ts). */
  clientIp?: string;
};

export async function refreshSession(refreshToken: string, deps: RefreshDeps = {}): Promise<RefreshOutcome> {
  const now = deps.now ?? Date.now;
  const { inflight, recent } = state();
  const key = await hash(refreshToken);

  for (const [k, entry] of recent) {
    if (now() - entry.at > REFRESH_GRACE_MS) recent.delete(k);
  }

  const cached = recent.get(key);
  if (cached) return { kind: "ok", tokens: cached.tokens };

  let pending = inflight.get(key);
  if (!pending) {
    pending = callRefresh(refreshToken, deps)
      .then((outcome) => {
        if (outcome.kind === "ok") recent.set(key, { tokens: outcome.tokens, at: now() });
        return outcome;
      })
      .finally(() => inflight.delete(key));
    inflight.set(key, pending);
  }
  return pending;
}

async function callRefresh(refreshToken: string, deps: RefreshDeps): Promise<RefreshOutcome> {
  const fetchFn = deps.fetch ?? globalThis.fetch;
  const baseUrl = deps.baseUrl ?? apiBaseUrl();
  let response: Response;
  try {
    response = await fetchFn(`${baseUrl}/api/v1/auth/refresh`, {
      method: "POST",
      headers: {
        "content-type": "application/json",
        accept: "application/json",
        ...(deps.clientIp ? { "x-forwarded-for": deps.clientIp } : {}),
      },
      body: JSON.stringify({ refreshToken }),
      cache: "no-store",
      signal: AbortSignal.timeout(10_000),
    });
  } catch {
    return { kind: "unavailable" };
  }

  if (response.ok) {
    return { kind: "ok", tokens: (await response.json()) as AuthTokens };
  }
  // 400/401 mean the token is invalid, expired or revoked; anything else is the API having trouble.
  return response.status === 400 || response.status === 401 ? { kind: "invalid" } : { kind: "unavailable" };
}

async function hash(value: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value));
  return Array.from(new Uint8Array(digest), (b) => b.toString(16).padStart(2, "0")).join("");
}

/** Test helper: forget all in-flight and recent refreshes. */
export function resetRefreshState(): void {
  const { inflight, recent } = state();
  inflight.clear();
  recent.clear();
}
