using System.Globalization;
using System.Text;
using TourDock.Domain.Bookings;
using TourDock.Domain.Tours;

namespace TourDock.Application.Notifications;

public sealed record EmailContent(string ToAddress, string ToName, string Subject, string Text, string Html);

/// <summary>Everything the texts need, gathered once per notification.</summary>
public sealed record BookingEmailDetails(
    Booking Booking, Tour Tour, string AgencyName, string TravelerName, SendNotificationMessage Message, string? WebBaseUrl)
{
    /// <summary>Short reference people can quote on the phone; the same one the web app shows.</summary>
    public string Reference => Booking.ReferenceFor(Booking.Id);

    /// <summary>Set for <see cref="NotificationKind.ReviewPosted"/>.</summary>
    public Domain.Reviews.Review? Review { get; init; }
}

/// <summary>
/// Plain-text and HTML versions of every booking email. Every value is HTML-encoded in the HTML part. Times are the
/// departure's local time at the tour, with the zone named, because that's when the traveler has to be there.
/// </summary>
public static class BookingEmails
{
    // The API image runs in invariant-globalization mode (no ICU), so named cultures aren't available.
    // The invariant culture formats day and month names in English, which is what these emails use.
    private static readonly CultureInfo En = CultureInfo.InvariantCulture;

    public static bool NotifiesAgency(NotificationKind kind) =>
        kind is NotificationKind.BookingConfirmed or NotificationKind.BookingCancelledByCustomer or NotificationKind.GuestsReduced
            or NotificationKind.ReviewPosted;

    public static bool NotifiesTraveler(NotificationKind kind) => kind is not NotificationKind.ReviewPosted;

    public static EmailContent ForTraveler(BookingEmailDetails d, Abstractions.UserContact traveler)
    {
        var b = d.Booking;
        var (subject, intro, extra) = d.Message.Kind switch
        {
            NotificationKind.BookingConfirmed => (
                $"Booking confirmed: {d.Tour.Title} on {ShortDate(d)}",
                $"Your booking with {d.AgencyName} is confirmed. See you on board!",
                (string?)null),
            NotificationKind.BookingCancelledByCustomer => (
                $"Booking cancelled: {d.Tour.Title}",
                "Your booking has been cancelled as requested.",
                RefundLine(d)),
            NotificationKind.BookingCancelledByAgency => (
                $"{d.AgencyName} cancelled your booking: {d.Tour.Title}",
                $"We're sorry: {d.AgencyName} had to cancel this departure."
                    + (string.IsNullOrWhiteSpace(b.CancellationReason) ? string.Empty : $" Reason given: {b.CancellationReason}"),
                RefundLine(d)),
            NotificationKind.GuestsReduced => (
                $"Booking updated: {d.Tour.Title}, now {Guests(b.Guests)}",
                $"Your booking now has {Guests(b.Guests)} ({Guests(d.Message.Guests ?? 0)} removed).",
                RefundLine(d)),
            NotificationKind.ReviewRequest => (
                $"How was {d.Tour.Title}?",
                $"Thanks for travelling with {d.AgencyName}! Tell other travelers how it went. It only takes a minute.",
                $"You can leave a review for {Domain.Reviews.Review.ReviewWindow.TotalDays:0} days."),
            _ => (
                $"Refund sent: {Money(d.Message.Amount, d.Message.Currency)}",
                $"We've sent your refund of {Money(d.Message.Amount, d.Message.Currency)} for {d.Tour.Title}.",
                "Depending on your bank, it can take 5 to 10 business days to appear on your statement."),
        };

        var badge = d.Message.Kind switch
        {
            NotificationKind.BookingConfirmed => ("Confirmed", EmailTone.Positive),
            NotificationKind.BookingCancelledByCustomer => ("Cancelled", EmailTone.Neutral),
            NotificationKind.BookingCancelledByAgency => ("Cancelled by the agency", EmailTone.Negative),
            NotificationKind.GuestsReduced => ("Booking updated", EmailTone.Warning),
            NotificationKind.ReviewRequest => ("How was it?", EmailTone.Positive),
            _ => ("Refund sent", EmailTone.Positive),
        };

        return Build(traveler.Email, traveler.DisplayName, subject, $"Hi {traveler.DisplayName},", intro, extra, d, badge,
            showMeetingPoint: d.Message.Kind == NotificationKind.BookingConfirmed);
    }

    public static EmailContent ForAgency(BookingEmailDetails d, string agencyName, string contactEmail)
    {
        var (subject, intro) = d.Message.Kind switch
        {
            NotificationKind.BookingConfirmed => ($"New booking: {d.Tour.Title} on {ShortDate(d)}", $"{d.TravelerName} booked and paid {Guests(d.Booking.Guests)}."),
            NotificationKind.BookingCancelledByCustomer => ($"Booking cancelled by traveler: {d.Tour.Title} on {ShortDate(d)}", $"{d.TravelerName} cancelled their booking. The seats are free again."),
            NotificationKind.ReviewPosted => (
                $"New {d.Review!.Rating}-star review: {d.Tour.Title}",
                $"{Reviews.AuthorNames.Shorten(d.TravelerName)} rated {d.Tour.Title} {d.Review.Rating} out of 5."
                    + (d.Review.Comment is null ? string.Empty : $" They wrote: \u201c{d.Review.Comment}\u201d")
                    + " You can reply publicly from your reviews page."),
            _ => ($"Booking reduced: {d.Tour.Title} on {ShortDate(d)}", $"{d.TravelerName} removed {Guests(d.Message.Guests ?? 0)}; the booking now has {Guests(d.Booking.Guests)}."),
        };

        var badge = d.Message.Kind switch
        {
            NotificationKind.BookingConfirmed => ("New booking", EmailTone.Positive),
            NotificationKind.BookingCancelledByCustomer => ("Cancelled", EmailTone.Negative),
            NotificationKind.ReviewPosted => ($"{d.Review!.Rating}-star review", EmailTone.Positive),
            _ => ("Booking reduced", EmailTone.Warning),
        };

        return Build(contactEmail, agencyName, subject, $"Hello {agencyName},", intro, extra: null, d, badge, showMeetingPoint: false, forAgency: true);
    }

    private static EmailContent Build(
        string to, string toName, string subject, string greeting, string intro, string? extra, BookingEmailDetails d, (string, EmailTone) badge,
        bool showMeetingPoint, bool forAgency = false)
    {
        var facts = new List<(string Label, string Value)>
        {
            ("Tour", d.Tour.Title),
            ("When", $"{LocalStart(d)} ({d.Tour.Location.TimeZoneId})"),
            ("Guests", $"{d.Booking.Guests}{(d.Booking.Type == BookingType.Private ? " (private trip)" : string.Empty)}"),
            ("Total", Money(d.Booking.TotalPrice.Amount, d.Booking.TotalPrice.Currency)),
            ("Reference", d.Reference),
        };
        if (showMeetingPoint)
        {
            facts.Insert(2, ("Meeting point", $"{d.Tour.Location.Address}, {d.Tour.Location.City}"));
        }

        var path = (forAgency, d.Message.Kind) switch
        {
            (true, NotificationKind.ReviewPosted) => $"/agency/{d.Booking.AgencyId}/reviews",
            (true, _) => $"/agency/{d.Booking.AgencyId}/bookings",
            (false, NotificationKind.ReviewRequest) => $"/bookings?tab=past#booking-{d.Booking.Id}",
            _ => "/bookings",
        };
        var link = d.WebBaseUrl is null ? null : $"{d.WebBaseUrl.TrimEnd('/')}{path}";
        var linkLabel = (forAgency, d.Message.Kind) switch
        {
            (true, NotificationKind.ReviewPosted) => "Reply to the review",
            (true, _) => "Open your bookings",
            (false, NotificationKind.ReviewRequest) => "Write a review",
            _ => "View your booking",
        };

        var text = new StringBuilder()
            .AppendLine(greeting).AppendLine()
            .AppendLine(intro);
        if (extra is not null)
        {
            text.AppendLine().AppendLine(extra);
        }

        text.AppendLine();
        foreach (var (label, value) in facts)
        {
            text.AppendLine($"{label}: {value}");
        }

        if (link is not null)
        {
            text.AppendLine().AppendLine($"{linkLabel}: {link}");
        }

        text.AppendLine().AppendLine("TourDock");

        var html = EmailLayout.Render(new EmailLayoutModel(Preheader: intro, Greeting: greeting, Intro: intro)
        {
            Badge = badge,
            Extra = extra,
            CardTitle = d.Tour.Title,
            Facts = [.. facts.Where(fact => fact.Label != "Tour")],
            Button = link is null ? null : (linkLabel, link),
            Reason = forAgency
                ? "You're receiving this email because your agency lists tours on TourDock."
                : "You're receiving this email because you booked a tour on TourDock.",
        });
        return new EmailContent(to, toName, subject, text.ToString(), html);
    }

    private static string? RefundLine(BookingEmailDetails d) =>
        d.Message.Amount is > 0
            ? $"A refund of {Money(d.Message.Amount, d.Message.Currency)} is on its way. We'll email you again when it's sent."
            : d.Booking.PaidAmount.Amount > 0 ? "Under the cancellation policy, no refund is due for this change." : null;

    private static string Guests(int count) => count == 1 ? "1 guest" : $"{count} guests";

    internal static string Money(decimal? amount, string? currency) =>
        currency == "EUR" ? $"€{(amount ?? 0).ToString("0.00", CultureInfo.InvariantCulture)}" : $"{(amount ?? 0).ToString("0.00", CultureInfo.InvariantCulture)} {currency}";

    private static DateTime Local(BookingEmailDetails d)
    {
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(d.Tour.Location.TimeZoneId, out var found) ? found : TimeZoneInfo.Utc;
        return TimeZoneInfo.ConvertTime(d.Booking.SlotStartsAt, zone).DateTime;
    }

    private static string LocalStart(BookingEmailDetails d) => Local(d).ToString("dddd d MMMM yyyy, HH:mm", En);

    private static string ShortDate(BookingEmailDetails d) => Local(d).ToString("ddd d MMM", En);
}
