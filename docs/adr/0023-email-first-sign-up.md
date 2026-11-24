# ADR 0023: Email-first sign-up

- **Status:** Accepted
- **Date:** 2026-11-10

## Context
- Sign-up answered `409 Auth.EmailTaken` for an address that already had an account. Anyone could use the form to
  check whether a given email address is a TourDock customer (account enumeration). Sign-in and the email change
  already gave the same answer either way.
- That answer can't be hidden while sign-up also signs you in: a taken address can't get a session for a new account.

## Decision
- **Sign-up only sends an email.** `POST /api/v1/auth/register` always answers `202 Accepted` with no body and no
  tokens. Validation (including the breached-password check) still answers `400`, but it doesn't depend on whether
  the address exists.
- **The inbox tells the truth:**
  - New address: an unconfirmed account and a confirmation link.
  - Address with an unconfirmed account: a new link (it replaces the old one). The password and name stay as they were.
  - Address with a confirmed account: a notice ("someone tried to sign up with your email"). Nothing changes.
- **Same timing:** for an existing address the password is still hashed, so the response takes about as long.
- **Sign-in needs a confirmed email** (with `Accounts:RequireConfirmedEmail`, default `true`). The right password on
  an unconfirmed account answers `403 Auth.SignInNeedsConfirmedEmail` and sends a new link. A wrong password gets the
  usual `Auth.InvalidCredentials`, so this reveals nothing to someone who doesn't know the password.
- After the link, the confirm page sends people to sign in. The web app shows "Check your inbox" after sign-up.

## Consequences
- **Trade-off:** one more step before the first sign-in (open the email, then sign in). In return the sign-up form
  can't reveal who has an account, and every account that can sign in has a working address.
- **Older unconfirmed accounts** can't sign in until they open a link; signing in sends them one.
- **Rate limits:** sign-up and sign-in stay under the per-IP auth limit, which also caps how many notices one client
  can trigger.
- **Not done:** password reset. A stranger who signs up first with someone else's address holds the password of that
  unconfirmed account. The owner can only confirm it, not take it over, until a reset flow exists.
