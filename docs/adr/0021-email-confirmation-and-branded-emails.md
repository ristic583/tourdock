# ADR 0021: Email confirmation and branded emails

- **Status:** Accepted; sign-up and sign-in changed by [ADR 0023](0023-email-first-sign-up.md)
- **Date:** 2026-11-09

## Context
- Anyone could sign up with any address and book straight away. Agencies then saw a traveler's name and email they
  couldn't trust, and booking emails could go to a stranger's inbox.
- The emails were bare HTML (a few paragraphs and a table) that didn't look like the site.

## Decision

**What needs a confirmed email**
- Signing up still signs you in, and you can browse right away. (Since ADR 0023, sign-up only sends the link, and signing in needs the confirmed address.)
- **Booking** and **registering an agency** need a confirmed address. The API answers `403 Auth.EmailNotConfirmed`.
  The web app shows a banner with "Resend link" until the address is confirmed.
- `Accounts:RequireConfirmedEmail` (default `true`) can switch the requirement off. The integration tests do, except
  the tests that cover confirmation.
- Accounts created before this change count as confirmed (data migration), so nobody is suddenly locked out of booking.

**The link**
- 32 random bytes, sent as base64url in `/verify-email?user=…&token=…`. Only a SHA-256 hash and an expiry (48 hours)
  are stored, in Identity's user tokens table. A database leak can't confirm anyone.
- **Single use, latest wins:** asking for a new link replaces the old one. Opening a link twice is fine.
- **No token in the outbox:** registration queues `SendEmailConfirmationMessage(userId)` through the outbox, and the
  token is created when the email is sent. A mail server hiccup only delays the email.
- **Confirming is anonymous:** the link may be opened on another device. The page confirms from the browser after it
  loads, not while rendering, so link previews don't spend it.
- The gate reads the flag from the database, so a confirmation counts at once, without a new access token.

**Branded emails**
- One layout (`EmailLayout`) for every email: brand bar, status chip, text, a details card and one button.
- Built for email clients, not browsers: nested tables, inline styles, a 600 px column, a "bulletproof" button, and no
  images or web fonts, so it renders without remote content. The link is also printed under the button.
- Every email keeps a plain-text version.

## Consequences
- **Trade-off:** one more step before the first booking. Browsing stays open, and the banner tells people what's missing.
- **Test setup:** E2E sign-ups confirm through Mailpit (`e2e/mailpit.ts`).
- **Not done:** no email change flow (it would need confirming the new address) and no per-account resend throttle
  beyond the per-IP auth rate limit.
