import { describe, expect, it } from "vitest";

import { siteOrigin } from "./env";
import { mcpAddresses } from "./mcp-connect";

describe("MCP addresses", () => {
  it("live on the site's own origin", () => {
    expect(mcpAddresses("https://tours.example.com/")).toEqual({
      public: "https://tours.example.com/mcp",
      account: "https://tours.example.com/mcp/account",
    });
  });

  it("use the configured public origin behind a reverse proxy, whatever host the request came in on", () => {
    const headers = new Headers({ host: "192.0.2.5:3000" });
    expect(siteOrigin(headers, "https://tours.example.com/")).toBe("https://tours.example.com");
  });

  it("otherwise follow the request's host and scheme", () => {
    expect(siteOrigin(new Headers({ host: "localhost:3000" }), undefined)).toBe("http://localhost:3000");
    expect(siteOrigin(new Headers({ "x-forwarded-host": "tours.example.com", "x-forwarded-proto": "https, http" }), "")).toBe(
      "https://tours.example.com",
    );
  });
});
