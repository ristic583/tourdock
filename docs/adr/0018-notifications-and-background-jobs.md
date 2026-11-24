# ADR 0018: Email notifications and background jobs

- **Status:** Accepted
- **Date:** 2026-11-07

## Context
Travelers expect an email when a booking is confirmed, cancelled or changed, and when a refund is sent. Agencies want to
hear about new bookings and cancellations. Sending email is a call to an outside system that can be slow or down, and it
must never send for a change that was rolled back, or lose an email for a change that was saved.

## Decision

**Domain events → outbox → email.**
- **Raising events:** the aggregates record what happened. `Booking` raises `BookingConfirmed`, `BookingCancelled` (with who
  cancelled and the refund) and `BookingGuestsReduced`; `Refund` raises `RefundSucceeded`. Events live on the `Entity` base
  class and are never persisted.
- **Saving them:** a `SaveChangesInterceptor` maps each event to an outbox message (ADR 0011) **in the same transaction** as
  the change. No handler has to remember to notify, and a rolled-back change never emails anyone.
- **Sending:** the existing outbox worker delivers the message to `SendNotificationHandler`, which looks up the booking, tour,
  agency and traveler and sends the emails.
- **What's queued:** messages carry ids only, never email addresses. Contacts are looked up when sending, so a deleted or
  anonymized account doesn't leave its address behind in queued data.

**Who gets what**

| Event | Traveler | Agency (its contact email) |
|---|---|---|
| Booking confirmed (paid) | ✅ with meeting point | ✅ new booking |
| Cancelled by the traveler | ✅ with refund | ✅ |
| Cancelled by the agency, or with its whole departure | ✅ with reason and full refund | – |
| Guests reduced | ✅ with partial refund | ✅ |
| Refund sent | ✅ | – |

An expired hold sends nothing: the traveler was on the page and saw it there. Agency emails name the traveler but don't
include the traveler's email address.

**Avoiding duplicates.** The outbox delivers at least once, and SMTP can't detect a duplicate.
- Each notification gets an id when it's queued. Every recipient has a key (`{id}.traveler`, `{id}.agency`) that is checked
  against `sent_notifications` before sending and recorded right after.
- A retry only sends what's still missing. A duplicate is only possible if the process crashes between the server accepting
  the email and that record being written.
- The key is also the `Message-ID`, so mail clients can drop a duplicate that does get through.

**Failures**
- A temporary error (connection refused, timeout, 4xx) throws, and the outbox retries with exponential backoff, parking the
  message after 10 attempts.
- A permanent 5xx rejection of the recipient is recorded as `Rejected` and not retried.

**Templates** are plain C#, producing a text part and a simple HTML part with every value HTML-encoded. There's no
Razor or template-engine dependency. Times are the departure's local time with the zone named, money is in EUR, and
there's a short booking reference.

**Sending: MailKit (MIT) behind `IEmailSender`.** Microsoft recommends it over `System.Net.Mail.SmtpClient`. Settings
(`Email:*`) are validated at startup.
- **Locally** (`docker compose up`, the dev container) every email goes to **Mailpit** at http://localhost:8025, and nothing
  leaves the machine.
- **When deployed** the API sends to an SMTP relay, which delivers for real through an email provider with SPF, DKIM
  and DMARC on a dedicated sending subdomain. The relay is separate infrastructure, shared with other apps.
- **Demo addresses:** the real deployment sets `Email:SkipReservedDomains`, so addresses on reserved demo domains
  (`example.com`, `*.test`, `*.invalid`, ...) are never sent to. The demo data uses them, and the bounces would hurt the
  sending domain's reputation.

**Background jobs stay hosted services in the API** (outbox, hold expiry, departure generation), coordinated through
PostgreSQL. `FOR UPDATE SKIP LOCKED` lets several instances share the outbox safely. No Hangfire, and no message broker:
a broker such as RabbitMQ pays off once several services consume the same events, or the volume outgrows one database
table. Neither is true here.

## Consequences
- ✅ Notifications can't be forgotten in a new use case that changes a booking, and can't go out for a change that rolled back.
- ✅ A mail outage delays emails; it never fails a booking or a payment.
- ⚠️ Delivery is "at least once, almost exactly once": a crash in the instant after the server accepts an email can send it again.
- ⚠️ A reminder email (e.g. 24 h before departure) needs a scheduled job that queries upcoming departures. Not built yet.
- ⚠️ No unsubscribe or notification preferences: these are transactional emails only. Marketing email would need consent
  tracking and an unsubscribe link.
