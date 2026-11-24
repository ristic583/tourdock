# ADR 0007: Authentication and authorization

- **Status:** Accepted
- **Date:** 2026-11-06

## Decision

**Accounts:** ASP.NET Core Identity (`users`, `roles` and related tables) in the same PostgreSQL database.
- Password policy: **length over composition** (NIST SP 800-63B), at least 10 characters with no forced character classes.
- **Lockout** for 15 minutes after 5 failed attempts.
- Login returns the same error for an unknown email and a wrong password, so attackers can't find out which emails exist.

**Tokens**
- **Access token:** a JWT (HMAC-SHA256), valid for **15 minutes**. Claims: `sub`, `email`, `name`, `role`.
  The signing key comes from configuration (`Jwt__SigningKey`), is validated at startup (≥ 32 characters), and is never in source.
- **Refresh token:** 64 random bytes, valid for **14 days**. Only its **SHA-256 hash** is stored, so a database leak doesn't expose usable tokens.
  - **Rotation:** every refresh revokes the old token and issues a new one in the same *family*.
  - **Reuse detection:** if an already-rotated token is presented again (a likely theft), the **whole family is revoked**,
    and both the attacker and the legitimate session must sign in again.
  - **Logout** revokes the family.
- The API returns tokens in the response body. The Next.js BFF (see the frontend ADR, added with the frontend) will store them in httpOnly cookies.

**Authorization**
- One global role: **Admin**, seeded by migration. The admin account is created on startup from `Admin__Email` / `Admin__Password`, only when both are set.
- Every user can book tours.
- Agency permissions come from `AgencyMember (UserId, AgencyId, Role)` and are **checked against the database on each request**,
  not baked into the token. Removing someone from an agency takes effect immediately.
- Endpoints use authorization policies (`RequireAuthorization()`, `Admin`). Resource ownership (e.g. "this tour belongs to my agency")
  is checked in the Application layer.

**Abuse protection:** `/auth/*` is rate limited per client IP (fixed window, 10 requests per minute by default).

**Errors:** every failure is RFC 9457 ProblemDetails with a stable `code`. Validation failures include per-field `errors`.
Concurrency conflicts map to `409 Conflict`. Unexpected errors map to `500` without internal details.

## Consequences
- ✅ Stateless, short-lived access tokens. Stolen refresh tokens are detected. Lockout, rate limiting and uniform error messages slow down credential attacks.
- ⚠️ An access token stays valid for up to 15 minutes after logout or role removal. That's accepted, given the short lifetime.
- ⚠️ Rate limiting is in memory per instance. With several instances it should move to a shared store such as Redis.
- Deferred: email confirmation and password reset (need the notifications segment), external login providers.

## Amendment (2026-11-16): throttling instead of lockout
- The 15-minute lockout after 5 failures let anyone who knows an email lock its owner out, and its distinct answer
  revealed which addresses have accounts. Failures now throttle only the guessing address for that email (5 in
  15 minutes, then `429 Auth.TooManyAttempts`, the same for unknown emails). The owner signing in from elsewhere is
  unaffected. Identity's account lockout stays as a backstop against guessing from many addresses, at 50 failures.
- The client address the throttle and rate limits see is decided by the edge proxy ([ADR 0024](0024-edge-proxy-and-client-ip.md)).
