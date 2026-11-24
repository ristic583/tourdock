import { describe, expect, it } from "vitest";

import { describeScopes, redirectTarget } from "./oauth-consent";

describe("OAuth consent", () => {
  it("describes known scopes and ignores unknown ones", () => {
    expect(describeScopes("bookings offline_access admin")).toHaveLength(1);
    expect(describeScopes("agency:read agency:write")).toHaveLength(2);
  });

  it("names where the decision leads and flags apps on this computer", () => {
    expect(redirectTarget("https://claude.ai/api/mcp/auth_callback")).toEqual({ label: "claude.ai", loopback: false });
    expect(redirectTarget("http://127.0.0.1:51234/callback")).toEqual({ label: "an app on this computer", loopback: true });
    expect(redirectTarget("http://evil.example.com/cb")).toBeNull();
    expect(redirectTarget("javascript:alert(1)")).toBeNull();
  });
});
