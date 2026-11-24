import { describe, expect, it } from "vitest";

import { isSameOrigin, traceparent, upstreamPath } from "./bff";

describe("upstreamPath", () => {
  it("forwards API v1 paths", () => {
    expect(upstreamPath(["api", "v1", "me", "bookings"])).toBe("api/v1/me/bookings");
    expect(upstreamPath(["api", "v1", "tours", "a b"])).toBe("api/v1/tours/a%20b");
  });

  it("never exposes auth or webhook endpoints", () => {
    expect(upstreamPath(["api", "v1", "auth", "login"])).toBeNull();
    expect(upstreamPath(["api", "v1", "AUTH", "refresh"])).toBeNull();
    expect(upstreamPath(["api", "v1", "webhooks", "payments"])).toBeNull();
  });

  it("rejects anything outside api/v1 and path tricks", () => {
    expect(upstreamPath(["health", "ready"])).toBeNull();
    expect(upstreamPath(["api", "v1", "..", "..", "openapi"])).toBeNull();
    expect(upstreamPath(["api", "v1", "tours%2F..%2Fauth"].map(decodeURIComponent))).toBeNull();
  });
});

describe("isSameOrigin", () => {
  it("trusts Sec-Fetch-Site when the browser sends it", () => {
    expect(isSameOrigin(new Headers({ "sec-fetch-site": "same-origin" }))).toBe(true);
    expect(isSameOrigin(new Headers({ "sec-fetch-site": "cross-site", origin: "http://localhost:3000", host: "localhost:3000" }))).toBe(false);
  });

  it("falls back to comparing Origin with Host", () => {
    expect(isSameOrigin(new Headers({ origin: "http://localhost:3000", host: "localhost:3000" }))).toBe(true);
    expect(isSameOrigin(new Headers({ origin: "https://evil.example.com", host: "localhost:3000" }))).toBe(false);
    expect(isSameOrigin(new Headers({ host: "localhost:3000" }))).toBe(false);
  });
});

describe("traceparent", () => {
  it("keeps a valid incoming trace context", () => {
    const incoming = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
    expect(traceparent(incoming)).toBe(incoming);
  });

  it("starts a new trace for missing, malformed or all-zero values", () => {
    for (const bad of [null, "nonsense", "00-00000000000000000000000000000000-00f067aa0ba902b7-01"]) {
      expect(traceparent(bad)).toMatch(/^00-[0-9a-f]{32}-[0-9a-f]{16}-01$/);
      expect(traceparent(bad)).not.toBe(bad);
    }
  });
});
