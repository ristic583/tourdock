# ADR 0012: Web frontend with Next.js and a backend-for-frontend

- **Status:** Accepted
- **Date:** 2026-11-06

## Context
TourDock needs a web UI for two audiences: travelers who search and book tours, and agencies who run their back office.
The public pages (home, search, tour detail) must be indexable by search engines and fast on a phone.
The signed-in pages handle money and personal data, so a cross-site scripting (XSS) bug must not be able to steal a session.
The API (ADR 0007) issues a 15-minute JWT access token and a 14-day refresh token that's rotated on every use,
and presenting an already-used refresh token revokes the whole session.

## Decision

**Stack** (in `web/`):

| Concern | Choice |
|---|---|
| Framework | Next.js (App Router, React Server Components, Cache Components), TypeScript |
| Styling | Tailwind CSS v4 with the design tokens as CSS custom properties (light and dark), shadcn/ui components on Radix |
| Server state | TanStack Query (client components), `fetch` in Server Components |
| API client | Request types generated from the API's OpenAPI document (`openapi-typescript`) and used through `openapi-fetch` |
| Forms | React Hook Form + Zod; API validation errors (ProblemDetails `errors`) are mapped back onto the same fields |
| Tests | Vitest + Testing Library for units and components, Playwright for end-to-end flows against Docker Compose |
| Runtime | Node.js LTS, `output: "standalone"`, multi-stage Docker image running as a non-root user |

**Backend-for-frontend (BFF) with httpOnly cookies.** The browser never sees a token.
- Sign-in, registration and sign-out are **Server Actions**. They call the API server-side and store both tokens in
  `httpOnly`, `Secure`, `SameSite=Lax` cookies that expire with the tokens.
- Server Components read the access token from the cookie and call the API directly.
- Client components call `/api/bff/api/v1/...`, a route handler that adds the `Authorization` header server-side and
  forwards the request. It refuses the auth and webhook endpoints (their responses carry tokens or aren't for browsers),
  path tricks, and cross-site writes (`Sec-Fetch-Site` / `Origin` check on top of `SameSite=Lax`).
- Why: a token in `localStorage` or a readable cookie is one XSS bug away from being stolen and replayed from anywhere.
  An `httpOnly` cookie can't be read by scripts, and `SameSite=Lax` plus the origin check stop cross-site request forgery.

**Token refresh.** The proxy (`src/proxy.ts`, formerly "middleware") runs before every page, Server Action and BFF call.
When the access token is missing or expires within a minute, it refreshes it and writes the new cookies both to the
response and to the request being rendered (Server Components can't set cookies themselves).
Because one page load triggers several requests at once, and reusing a rotated refresh token revokes the session,
**refreshes are serialized per refresh token**: concurrent requests share one in-flight call, and for 30 seconds a request
that still carries the old token gets the new tokens instead of calling the API again.
That state lives in the Node.js process. It's correct for one `web` container; running several replicas would need sticky
sessions or a shared store (for example Redis).

**Server rendering for SEO.** Public pages render on the server, so crawlers and link previews get complete HTML with
real titles and descriptions. With Cache Components, the static part of each page is prerendered into a shell and
anything that reads the session (the account menu, for example) streams in behind a `<Suspense>` boundary,
so a signed-in header doesn't make the whole page dynamic.

**Protected routes.** The proxy sends anonymous visitors of `/account`, `/bookings` and `/agency` to the login page
(with a validated `next` path, so there's no open redirect). Pages and Server Actions still check the user themselves
through a small data-access layer, because the proxy is a convenience, not the security boundary.

**Errors.** API errors are RFC 9457 ProblemDetails. The client turns them into one `ApiError` type with the stable `code`,
a user-facing message (friendly text for known codes, then the API's `detail`, then a message per status) and field errors.

**Catalog state lives in the URL.** Search, price range, booking type, sort and page are query parameters, parsed by one
client-safe module (`web/src/lib/catalog-search.ts`) on the server page and in the filter sidebar. Every result page can
be shared and bookmarked, and Back works. On the tour page, the booking panel keeps `guests` and `date` in the URL with
`history.replaceState`.

**Seats on search cards** come from the search response itself: each result carries its `nextDeparture` with live
seat counts ([ADR 0014](0014-live-seats-in-search.md)), so a results page is one API call. `nextDeparture: null` on a
listed tour means it sold out since the search was cached, shown as "Sold out for these dates".

**Booking and checkout.** Reserve only holds the seats (`POST /bookings` with an Idempotency-Key that is reused on
retry), then the traveler lands on `/bookings/{id}/checkout`. That page counts down the hold and starts the payment;
a 503 from the payment provider is retried with 1/2/4 s backoff (the API reuses a pending payment, so this is safe).
The payment page opens in the same tab with `returnUrl` set to this checkout page ([ADR 0015](0015-payment-return-url-and-status.md)).
Back on it, the booking is re-read every second for a few tries while `lastPaymentStatus` is `Pending` (the webhook may
land a moment after the redirect); a `Failed` payment shows its decline reason and offers to try again. An expired hold
is re-read until the API marks it Expired. Times are always formatted in the tour's time zone, never from server-formatted UTC.

**Agency back office and admin.** `/agency/{agencyId}/*` pages check membership on the server (`requireAgency`), read
with the user's token, and write through the BFF followed by `router.refresh()`; there is no client cache to keep in
sync. KPIs come from `GET /agencies/{id}/stats` ([ADR 0016](0016-agency-stats-endpoint.md)): this month for revenue, the next
7 days for departures and vehicles, the next 30 days for occupancy. `/admin/*` needs the
`Admin` role and renders the 404 page otherwise. Per-user pages set `instant = false`, so Cache Components never tries
to prerender them.

**Display currency and weather** ([ADR 0017](0017-external-apis-exchange-rates-weather.md)). A currency picker in the
header stores the choice in a plain `td-currency` cookie (it isn't sensitive) and calls `router.refresh()`, so server
components render every price in the right currency on first paint, without a flash. Pages read `GET /exchange-rates`
once per render and convert with one shared helper (rounding like the API). EUR always stays the main price; the
converted amount is a muted "≈ $50.24" with a note giving the ECB date (or saying the rate is the last available one)
and that the charge is in EUR. Checkout and My bookings show EUR only. With no rates, the converted line just isn't shown.
The tour page has a weather card for the departure picked in the booking panel (the first bookable one before that),
read through the BFF with TanStack Query. It shows a short summary, temperature, chance of rain, wind and gusts, plus a
warning for rough conditions. More than 14 days out it says when the forecast becomes available. If the weather service
is unavailable, or the departure is in the past, the card isn't shown.

**Reviews** ([ADR 0019](0019-reviews-and-moderation.md)).
- **Tour page:** the server renders a rating summary (average, count, a bar for each star rating) and the first five
  reviews. Sorting and "Show more" load further pages through the BFF, using the same page size.
- **Reporting:** signed-in visitors can report a review in a dialog. It stays visible until an admin decides.
- **My bookings:** the Past tab offers "Write a review" while the booking's `reviewableUntil` hasn't passed, and "Edit
  review" while the review's `editableUntil` hasn't passed. Deleting is always possible. The star picker is a radio group,
  so it works with the keyboard and screen readers. The review-request email links straight to the booking on that tab.
- **Agency back office:** a Reviews page where managers reply publicly. Hidden reviews are shown, marked as hidden.
- **Admin:** a moderation queue (reported first; hide with a reason, show again, or close the reports) and a read-only
  audit log, with tabs alongside Agencies.

**End-to-end tests** (`web/e2e`, Playwright) run against the compose stack in the Playwright image; in CI they run
in the Docker job, which only runs for `main`.

## Consequences
- ✅ Tokens can't be read by page scripts; an XSS bug can act within the page but can't take the session elsewhere.
- ✅ Public pages are indexable and render useful HTML without JavaScript.
- ✅ The API stays a plain JSON API with Bearer tokens; the BFF is the only cookie-aware part.
- ⚠️ Every client-side API call takes an extra hop through the Next.js server.
- ⚠️ The refresh serialization is per process (see above).
- ⚠️ All API calls come from the web server, so the BFF passes the visitor's IP as `X-Forwarded-For`.
  The API trusts that header only from the web container's fixed address (ADR 0020).
- ⚠️ The API's OpenAPI document declares response bodies for catalog, bookings, payments, agency stats, exchange rates, weather and reviews only, so the
  rest (auth, agency back office, admin) are still written by hand in `web/src/lib/api/contracts.ts`. The generated
  types say `number | string` for every number (the API also reads numbers from strings); `Wire<T>` narrows them back.
