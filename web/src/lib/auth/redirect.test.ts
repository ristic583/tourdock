import { describe, expect, it } from "vitest";

import { loginPath, safeNextPath } from "./redirect";
import { isProtectedPath } from "./routes";

describe("safeNextPath", () => {
  it.each([
    ["/bookings", "/bookings"],
    ["/tours/42?date=2026-06-14", "/tours/42?date=2026-06-14"],
    ["//evil.example.com", "/"],
    ["/\\evil.example.com", "/"],
    ["https://evil.example.com", "/"],
    ["javascript:alert(1)", "/"],
    ["/ok\nSet-Cookie: x", "/"],
    [undefined, "/"],
  ])("%s → %s", (input, expected) => {
    expect(safeNextPath(input)).toBe(expected);
  });

  it("builds login links that come back to the page", () => {
    expect(loginPath("/bookings?tab=past")).toBe("/login?next=%2Fbookings%3Ftab%3Dpast");
    expect(loginPath("/")).toBe("/login");
    expect(loginPath("//evil.example.com")).toBe("/login");
  });
});

describe("isProtectedPath", () => {
  it("matches protected sections and their sub-pages only", () => {
    expect(isProtectedPath("/bookings")).toBe(true);
    expect(isProtectedPath("/agency/tours/1")).toBe(true);
    expect(isProtectedPath("/agencyx")).toBe(false);
    expect(isProtectedPath("/")).toBe(false);
    expect(isProtectedPath("/login")).toBe(false);
  });
});
