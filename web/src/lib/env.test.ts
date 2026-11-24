import { afterEach, describe, expect, it, vi } from "vitest";

import { cookiesSecure } from "./env";

describe("cookiesSecure", () => {
  afterEach(() => vi.unstubAllEnvs());

  it("is on by default", () => {
    vi.stubEnv("AUTH_COOKIE_SECURE", undefined);
    vi.stubEnv("PUBLIC_ORIGIN", undefined);
    expect(cookiesSecure()).toBe(true);
  });

  it("can be turned off for a plain-HTTP host", () => {
    vi.stubEnv("AUTH_COOKIE_SECURE", "false");
    vi.stubEnv("PUBLIC_ORIGIN", "http://192.0.2.10:3000");
    expect(cookiesSecure()).toBe(false);
  });

  it("stays on when the public origin is HTTPS, even if the flag says false", () => {
    vi.stubEnv("AUTH_COOKIE_SECURE", "false");
    vi.stubEnv("PUBLIC_ORIGIN", "https://tours.example.com");
    expect(cookiesSecure()).toBe(true);
  });
});
