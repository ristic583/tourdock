# ADR 0019: Reviews and moderation

- **Status:** Accepted
- **Date:** 2026-11-07

## Context
Reviews drive bookings on a marketplace, so they have to be trustworthy:
- Only real travelers should be able to write them.
- Agencies must not be able to bury bad ones.
- Competitors must not be able to get good ones removed.
- Personal data must not leak.

The `Review` entity and table existed since ADR 0006; this adds the use cases, moderation and the admin audit log.

## Decision

**Who can review, and when.** Only the traveler on a **completed** booking can review it, once per booking, within
**90 days** of the trip. The domain enforces this, and a unique index on `booking_id` stops double submits. The author can
**edit for 14 days** and **delete any time**, because it's their content. The review stores when the trip took place
("Travelled June 2026").

**What others see.**
- `GET /tours/{id}/reviews` returns a rating summary (average, count, how many of each star) and a page of visible reviews,
  sorted newest, highest or lowest.
- Authors appear as **"Ana M."**: other travelers never see full names or email addresses.
- Hidden reviews never appear and don't count towards ratings.
- Posting, editing, deleting, hiding and unhiding a review all clear the catalog cache, so search averages update right away.

**Fairer "sort by rating".** Search ranks by a Bayesian average:
`(avg × n + 4.0 × 5) / (n + 5)`. Every tour starts with five imaginary 4-star reviews. One 5★ review then scores 4.17,
while 200 reviews averaging 4.8 score 4.78. A brand-new tour with a single rave review no longer tops the list.

**Agencies** can post one public reply per review and edit or remove it (managers only). They **can't hide or edit
reviews**: that would be a conflict of interest. Their review list includes hidden reviews, marked as such, so they know.

**Moderation.**
- **Reports:** any signed-in user can report a review for one reason: spam, offensive, personal information, not about
  the tour, or other. One report per user and review; reporting again is accepted with no further effect; authors can't
  report their own review.
- **Reports never hide anything automatically.** A competitor with a few accounts could otherwise remove good reviews.
- **Admin queue:** reported reviews show most-reported first, with the reasons given.
- **Admin actions:** an admin can **hide** a review (a reason is required), **unhide** it, or **dismiss** the reports.
  Hiding or dismissing resolves the open reports.

**Admin audit log (`admin_audit_log`).** Every admin action records who did what to which item, why, and when, in the
**same transaction** as the change. It covers review moderation and also approving and suspending agencies. The
application never updates or deletes entries. Admins can read it at `GET /admin/audit-log`.

**Emails (ADR 0018).**
- When a booking is marked completed, the traveler gets "How was …?" with a link to review it.
- The agency is told about each new review, with a link to reply.
- Both come from domain events (`BookingCompleted`, `ReviewPosted`) through the outbox. If the author deletes the review
  before the email is sent, the agency's email is skipped.

## Consequences
- ✅ Every review comes from a completed, paid trip. Neither agencies nor brigades of reports can quietly remove reviews.
- ✅ Moderation is traceable after the fact.
- ⚠️ Deleting is hard delete, so a deleted review is gone, including its reports. Moderation history survives in the audit
  log only when an admin acted on it.
- ⚠️ No automatic content screening: profanity, links and phone numbers rely on reports. A text filter, or a
  moderation API that holds suspicious reviews for approval, would be the next step.
- ⚠️ When account deletion arrives, reviews should be anonymized ("Former traveler"), not deleted, so ratings stay stable.
- ⚠️ Ratings are computed on every query (with an index). At large scale they'd move to a stored per-tour summary,
  updated by the same events.
