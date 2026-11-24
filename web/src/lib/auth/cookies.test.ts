import { afterEach, describe, expect, it, vi } from "vitest";

import {
  ACCESS_COOKIE,
  REFRESH_COOKIE,
  clearedCookies,
  decodeCookieValue,
  encodeCookieValue,
  readTokenCookies,
  tokenCookies,
} from "./cookies";

describe("auth cookies", () => {
  afterEach(() => vi.unstubAllEnvs());

  it("stores both tokens as httpOnly, Secure, SameSite=Lax cookies that expire with the tokens", () => {
    const cookies = tokenCookies({
      accessToken: "a",
      accessTokenExpiresAt: "2030-01-01T00:15:00Z",
      refreshToken: "r",
      refreshTokenExpiresAt: "2030-01-15T00:00:00Z",
    });

    expect(cookies.map((c) => [c.name, decodeCookieValue(c.value)])).toEqual([
      [ACCESS_COOKIE, "a"],
      [REFRESH_COOKIE, "r"],
    ]);
    for (const c of cookies) {
      expect(c.options).toMatchObject({ httpOnly: true, secure: true, sameSite: "lax", path: "/" });
    }
    expect(cookies[1].options.expires.toISOString()).toBe("2030-01-15T00:00:00.000Z");
  });

  it("can drop Secure for plain-HTTP deployments", () => {
    vi.stubEnv("AUTH_COOKIE_SECURE", "false");
    expect(clearedCookies().every((c) => c.options.secure === false && c.value === "")).toBe(true);
  });

  it("round-trips base64 refresh tokens through cookie-safe values", () => {
    const token = "fake+refresh/token+value==";
    const value = encodeCookieValue(token);
    expect(value).toMatch(/^[A-Za-z0-9_-]+$/);
    expect(decodeCookieValue(value)).toBe(token);

    const store = new Map([[REFRESH_COOKIE, { value }]]);
    expect(readTokenCookies(store)).toEqual({ accessToken: undefined, refreshToken: token });
  });

  it("ignores values that weren't written by us", () => {
    expect(decodeCookieValue("%2BjEq")).toBeUndefined();
    expect(decodeCookieValue("")).toBeUndefined();
  });
});
