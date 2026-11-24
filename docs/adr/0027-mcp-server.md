# ADR 0027: MCP server for AI agents

- **Status:** Accepted
- **Date:** 2026-11-24

## Context

Travelers increasingly ask an AI assistant of their own (Claude, ChatGPT, an IDE agent) instead of opening a website.
The Model Context Protocol (MCP) is how those assistants call outside tools. TourDock already has read-only agent tools
for its own travel assistant (ADR 0026); offering the same tools over MCP lets any assistant search tours and check
departures on a traveler's behalf.

## Decision

**Inside the API, at `/mcp`.** The official C# SDK (`ModelContextProtocol.AspNetCore`) maps a Streamable HTTP endpoint in
the existing API, so it shares DI, the query handlers, rate limiting, security headers and logging. No second service to
deploy.

**The same tools, defined once.** `tools/list` and `tools/call` are custom handlers over `IAgentTools`: `search_tours`,
`get_tour` and `get_departures`, with the same JSON Schemas and descriptions the assistant sees. Results are plain text
(MCP has no citations), with each tour's page link. Every tool is annotated read-only, idempotent and closed-world.
Bad input and unknown tools come back as error results the agent can read, never as protocol errors.

**Stateless.** Each request stands alone (`Stateless = true`): no session state, so any API instance can answer, and
nothing needs cleaning up.

**Anonymous for now, with its own limit.** Like public search, the tools need no account. `/mcp` states
`AllowAnonymous()` explicitly (the endpoint-authorization test now covers it) and has a per-client rate-limit policy
(`mcp`, 30 calls a minute) on top of the global one.

**Reachable through the edge.** The edge proxy (ADR 0024) routes `/mcp` straight to the API and everything else to the
web app, setting the client address itself. The edge got a fixed address, and the API trusts `X-Forwarded-For` from it
as it does from the web container, so rate limits see the real client.

**Agency-written text is data.** The server instructions tell agents that tour texts come from agencies and are not
instructions; the tools are read-only, so injected text can't make them change anything.

**Next: signed-in tools (plan phase D).** Starting a booking, listing a traveler's trips and agency tools need OAuth 2.1
as the MCP authorization spec describes (protected resource metadata, an authorization server, PKCE, scopes). That is a
separate decision.

## Consequences

- ✅ Any MCP client can use TourDock with one URL; the travel assistant and MCP clients get the same answers.
- ✅ Nothing new to run; stateless requests scale with the API.
- ⚠️ Anonymous tools can be scraped; the rate limits bound it, and the data is the public catalog anyway.
- ⚠️ No booking or personal data over MCP until OAuth exists.
