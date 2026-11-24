import { describe, expect, it } from "vitest";

import nextConfig, { contentSecurityPolicy } from "../../next.config";

describe("Content-Security-Policy", () => {
  it("is sent on every page, and only the consent page lets a form lead to another site", async () => {
    const rules = await nextConfig.headers!();
    expect(rules).toHaveLength(2);
    for (const rule of rules) expect(rule.headers.map((h) => h.key)).toContain("Content-Security-Policy");
    const csp = (source: string) => rules.find((r) => r.source === source)!.headers.find((h) => h.key === "Content-Security-Policy")!.value;
    expect(csp("/((?!oauth/consent).*)")).toContain("form-action 'self';");
    expect(csp("/oauth/consent")).toContain("form-action 'self' https: http://localhost:* http://127.0.0.1:*");
  });

  it("blocks foreign scripts, plugins, framing and base hijacking in production", () => {
    const csp = contentSecurityPolicy(false);
    expect(csp).toContain("script-src 'self' 'unsafe-inline'");
    expect(csp).not.toContain("unsafe-eval");
    expect(csp).toContain("object-src 'none'");
    expect(csp).toContain("frame-ancestors 'none'");
    expect(csp).toContain("base-uri 'self'");
    expect(csp).toContain("connect-src 'self'");
  });
});
