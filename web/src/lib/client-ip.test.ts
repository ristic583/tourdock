import { describe, expect, it } from "vitest";

import { clientIp, forwardedForHeader } from "./client-ip";

describe("clientIp", () => {
  it("takes the X-Forwarded-For entry added by the nearest hop", () => {
    expect(clientIp(new Headers({ "x-forwarded-for": "198.51.100.66, 203.0.113.7" }))).toBe("203.0.113.7");
  });

  it("falls back to X-Real-IP, then to nothing", () => {
    expect(clientIp(new Headers({ "x-real-ip": "198.51.100.4" }))).toBe("198.51.100.4");
    expect(forwardedForHeader(new Headers())).toEqual({});
  });
});
