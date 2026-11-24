# ADR 0028: OAuth 2.1 for the MCP server

- **Status:** Accepted
- **Date:** 2026-11-24

## Context

The MCP server (ADR 0027) offered public catalog tools only. To let a traveler's own AI assistant see their bookings,
hold seats or cancel, or let an agency member check bookings and answer reviews, the assistant needs to act **for a
user**, with that user's consent, and without ever seeing their password. The MCP authorization spec (2026-07-28)
makes the MCP server an OAuth 2.1 resource server and lets clients find the authorization server on their own.

## Decision

**Two MCP endpoints.** `/mcp` stays anonymous (catalog tools). `/mcp/account` requires an access token issued for it
and lists the account tools the token's scopes allow. A client that wants personal tools connects to `/mcp/account`.

**OpenIddict inside the API as the authorization server.** Users already live in ASP.NET Core Identity in the same
database, so no second identity system. Authorization code flow with PKCE (S256 only, `plain` removed), rotating refresh
tokens, 15-minute access tokens, and resource indicators (RFC 8707): tokens carry `aud = <origin>/mcp/account`, and
the validator refuses any other audience. The API's own tokens for the web app are never accepted on `/mcp/account`,
and its tokens are never passed through. OpenIddict serves RFC 8414 and OpenID discovery metadata; the authorization
response includes `iss` (RFC 9207).

**Scopes**: `bookings` (see bookings, hold seats, cancel after confirmation), `agency:read` (bookings, stats, reviews of
the user's agencies), `agency:write` (reply to reviews). `offline_access` is always granted so clients get a refresh
token. Scopes are checked once, in `AgentTools`; the account tools then run as the user through the normal handlers,
so ownership and agency-access checks still apply ("not yours" stays "not found").

**Discovery as the spec describes.** A 401 on `/mcp/account` carries
`WWW-Authenticate: Bearer resource_metadata="<origin>/.well-known/oauth-protected-resource/mcp/account"`; that
document (served by the MCP SDK's handler) names the authorization server and scopes.

**Client registration: Dynamic Client Registration (RFC 7591) for now.** Claude, Claude Code, ChatGPT and the MCP
Inspector all register this way today. The current MCP spec prefers Client ID Metadata Documents and marks DCR as
deprecated; supporting CIMD means fetching a URL the client chooses, which needs SSRF protection (block private
addresses, size and time limits, no redirects) and an exception to our rule that the server never fetches
user-supplied URLs. Left for later. Registration is anonymous and rate-limited (10 an hour per address), creates
public clients only (no secret, PKCE required), and accepts HTTPS or loopback redirect URIs. A loopback URI is stored
without its port, so a native client like Claude Code may come back on any port.

**Consent in the web app, sign-in from the web session.** The edge serves the web app and `/connect` from one origin,
so the authorization endpoint recognises a signed-in visitor from the web app's session cookie (the API token in an
httpOnly, SameSite=Lax cookie), validated like a Bearer token. The flow:
1. OpenIddict validates the authorization request and parks it (request caching); the endpoint gets its id.
2. If the visitor already allowed this app for these scopes, the code is issued at once.
3. Otherwise they go to `/oauth/consent` in the web app, which makes them sign in first and shows the app's name, what
   it will be able to do, and where they'll be sent back (with a warning for an app on their own computer).
4. The page posts the decision to `/connect/authorize` with the parked request's id. The endpoint accepts it only
   from the same origin, from the signed-in visitor, and only if the request still asks for exactly what the page
   showed. Then it remembers the consent and issues the code.

**Agents never pay and confirm before acting.** `start_booking` only holds seats and returns the checkout link for the
traveler; the same request on the same day returns the same booking. `cancel_booking` previews the refund unless called
with `confirm: true`, and its description tells the agent to ask first. `reply_to_review` asks for approval of the
exact text. Tour texts and reviews come back as data, and the server instructions say so.

**Keys.** `OAUTH_SIGNING_KEY_PEM` (RSA) and `OAUTH_ENCRYPTION_KEY` (256-bit, base64) keep tokens valid across
restarts; without them the keys are new on every start and connected apps sign in again. TLS ends at the reverse proxy in
front of the edge, so the API accepts plain HTTP from the edge and publishes the HTTPS origin as the issuer.

## Consequences

- ✅ Any current MCP client can connect a user's account with the user's consent; tokens are short-lived, bound to
  one resource and scoped.
- ✅ No second identity system and no second sign-in; the account tools reuse every existing rule.
- ⚠️ DCR is open by design (anyone can register a client); a client gets nothing without the user's consent. Unused
  registrations are not cleaned up yet.
- ⚠️ No CIMD yet (see above), and no page where a user lists and revokes connected apps (consent is remembered;
  revoking means deleting the authorization).
- ⚠️ The consent page's CSP allows forms to lead to any HTTPS site or a loopback address, because browsers apply
  `form-action` to the redirect back to the app. Signing in continues to `/oauth/...` with a full page load, so the page
  gets that policy instead of keeping the sign-in page's.
