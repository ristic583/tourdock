import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import type { AuthTokens } from "@/lib/api/contracts";

import { REFRESH_GRACE_MS, refreshSession, resetRefreshState } from "./refresh";

function tokens(n: number): AuthTokens {
  return {
    accessToken: `access-${n}`,
    accessTokenExpiresAt: "2030-01-01T00:15:00Z",
    refreshToken: `refresh-${n}`,
    refreshTokenExpiresAt: "2030-01-15T00:00:00Z",
  };
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
}

describe("refreshSession", () => {
  let now = 1_000_000;
  const clock = () => now;

  beforeEach(() => resetRefreshState());
  afterEach(() => resetRefreshState());

  it("sends concurrent refreshes with the same token to the API only once", async () => {
    let release!: () => void;
    const gate = new Promise<void>((resolve) => (release = resolve));
    const fetch = vi.fn(async () => {
      await gate;
      return json(tokens(2));
    });
    const deps = { fetch, baseUrl: "http://api", now: clock };

    const results = Promise.all([1, 2, 3, 4, 5].map(() => refreshSession("refresh-1", deps)));
    release();

    for (const outcome of await results) expect(outcome).toEqual({ kind: "ok", tokens: tokens(2) });
    expect(fetch).toHaveBeenCalledTimes(1);
    const [url, init] = fetch.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("http://api/api/v1/auth/refresh");
    expect(JSON.parse(init.body as string)).toEqual({ refreshToken: "refresh-1" });
  });

  it("returns the rotated tokens to a late request that still carries the old token", async () => {
    const fetch = vi.fn(async () => json(tokens(2)));
    const deps = { fetch, baseUrl: "http://api", now: clock };

    await refreshSession("refresh-1", deps);
    now += REFRESH_GRACE_MS - 1;
    expect(await refreshSession("refresh-1", deps)).toEqual({ kind: "ok", tokens: tokens(2) });
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it("calls the API again once the grace period is over", async () => {
    const fetch = vi.fn(async () => json(tokens(2)));
    const deps = { fetch, baseUrl: "http://api", now: clock };

    await refreshSession("refresh-1", deps);
    now += REFRESH_GRACE_MS + 1;
    await refreshSession("refresh-1", deps);
    expect(fetch).toHaveBeenCalledTimes(2);
  });

  it("reports a rejected token as invalid and doesn't cache it", async () => {
    const fetch = vi.fn(async () => json({ status: 400, code: "Auth.InvalidRefreshToken" }, 400));
    const deps = { fetch, baseUrl: "http://api", now: clock };

    expect(await refreshSession("refresh-1", deps)).toEqual({ kind: "invalid" });
    expect(await refreshSession("refresh-1", deps)).toEqual({ kind: "invalid" });
    expect(fetch).toHaveBeenCalledTimes(2);
  });

  it("reports network errors and 5xx as unavailable, so the session is kept", async () => {
    const down = { fetch: vi.fn(async () => Promise.reject(new TypeError("fetch failed"))), baseUrl: "http://api" };
    expect(await refreshSession("refresh-1", down)).toEqual({ kind: "unavailable" });

    const failing = { fetch: vi.fn(async () => json({}, 503)), baseUrl: "http://api" };
    expect(await refreshSession("refresh-1", failing)).toEqual({ kind: "unavailable" });
  });
});
