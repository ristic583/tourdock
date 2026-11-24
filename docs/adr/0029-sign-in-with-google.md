# ADR 0029: Sign in with Google

- **Status:** Accepted
- **Date:** 2026-11-24

## Context

Travelers sign in with an email and a password they had to create and confirm. "Continue with Google" removes both
steps for anyone with a Google account. It must not weaken what the password sign-in already guarantees:
- tokens never reach browser JavaScript (httpOnly cookies set by the BFF, ADR 0012);
- every account has an email its owner has proven.

## Decision

**Authorization code flow with PKCE, and the code is redeemed by the API.**
1. The web app's `/api/auth/google/start` makes a `state` and a PKCE verifier. It keeps them in a ten-minute httpOnly
   cookie scoped to `/api/auth/google`, then redirects to Google, asking only for `openid email profile`.
2. Google sends the browser to `/api/auth/google/callback`. The route checks the `state`, then posts the code, the
   verifier and the redirect URI to `POST /api/v1/auth/google`.
3. The API redeems the code with the **client secret**, which only the API has, and checks the ID token: Google's
   signature (keys from its published OpenID configuration, cached), issuer, this client as audience, and expiry.
4. The API answers like a password sign-in, and the web app sets the same cookies.

The web app learns the client id from `GET /api/v1/auth/providers`, so Google is configured in one place
(`GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`). Without both, the button isn't shown and the endpoint answers 503.

**Which account.** Google's subject id is stored as an ASP.NET Identity login (`user_logins`), so the person keeps their
account even if they change their Gmail address. On the first Google sign-in:
- With an account for the same email, it is **linked automatically**, but only when Google says the email is verified.
  Google has proven ownership of the address, which is all a confirmation link proves.
- Without one, a new account is created with the email confirmed and **no password**.
- An email Google hasn't verified signs nobody in.

**Account pre-hijacking.** Someone could sign up with another person's address and a password of their own, never
confirm it, and wait. When Google later vouches for the real owner, that account is linked, but **its password is
removed** first. Nobody ever proved they chose it. Confirmed accounts keep their password: their owner chose it.

## Consequences

- ✅ One click to sign up or sign in. No password to create, no confirmation email to wait for.
- ✅ The client secret and every identity decision stay in the API. The web app holds no Google secret.
- ✅ Tested end to end against a fake Google:
  - its configuration, keys and token endpoint;
  - single-use codes;
  - a token signed with a key Google doesn't publish.
- ⚠️ Accounts created through Google have no password. There's no "forgot password" flow yet to add one, so these
  travelers keep using Google.
- ⚠️ Each Google redirect URI must be registered in the Google Cloud console, once per origin. Locally that's
  `http://localhost:3000/api/auth/google/callback`; on the LAN deployment it's the `https` public URL.
- ⚠️ While the Google app is in "Testing", only its listed test users can sign in.
