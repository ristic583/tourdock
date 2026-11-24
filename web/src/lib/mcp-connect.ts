/**
 * Where AI apps connect to TourDock's MCP server (ADR 0027, 0028): public tools anonymously, account tools after the
 * traveler signs in and allows access. Both live on the site's own origin; the edge routes them to the API.
 */
export function mcpAddresses(origin: string): { public: string; account: string } {
  const base = origin.replace(/\/+$/, "");
  return { public: `${base}/mcp`, account: `${base}/mcp/account` };
}
