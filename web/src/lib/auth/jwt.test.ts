import { describe, expect, it } from "vitest";

import { isExpiring, jwtExpiresAt } from "./jwt";

function jwt(payload: object): string {
  const encode = (value: object) =>
    Buffer.from(JSON.stringify(value), "utf8").toString("base64url");
  return `${encode({ alg: "HS256", typ: "JWT" })}.${encode(payload)}.signature`;
}

describe("jwt helpers", () => {
  it("reads the exp claim in milliseconds", () => {
    expect(jwtExpiresAt(jwt({ sub: "u1", exp: 1_800_000_000, name: "Đurđa" }))).toBe(1_800_000_000_000);
  });

  it("returns null for malformed tokens", () => {
    expect(jwtExpiresAt("not-a-jwt")).toBeNull();
    expect(jwtExpiresAt("a.%%%.c")).toBeNull();
    expect(jwtExpiresAt(jwt({ sub: "no-exp" }))).toBeNull();
  });

  it("treats tokens that expire within the skew as expiring", () => {
    const now = 1_800_000_000_000;
    expect(isExpiring(undefined, now)).toBe(true);
    expect(isExpiring(jwt({ exp: now / 1000 + 30 }), now)).toBe(true);
    expect(isExpiring(jwt({ exp: now / 1000 + 600 }), now)).toBe(false);
  });
});
