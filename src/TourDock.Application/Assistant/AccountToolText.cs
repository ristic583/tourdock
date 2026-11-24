using System.Globalization;
using TourDock.Application.Agencies;
using TourDock.Application.Bookings;
using TourDock.Application.Common;
using TourDock.Application.Reviews;

namespace TourDock.Application.Assistant;

/// <summary>Account tool results as plain sentences. Personal data stays limited to what the user can already see.</summary>
internal static class AccountToolText
{
    private static readonly CultureInfo En = CultureInfo.InvariantCulture;

    /// <summary>Account results are not tour pages, so they are plain text rather than citable sources.</summary>
    public static AgentToolResult Result(string text) => new([], Note: text);

    public static string Booking(BookingResponse b) => string.Create(En,
        $"Booking {b.Id}: {b.TourTitle}, {b.LocalStart:ddd d MMM yyyy, HH:mm} local time, {b.Guests} guests ({b.Type}). ")
        + string.Create(En, $"Status {b.Status}. Total {Money(b.TotalPrice)}, paid {Money(b.PaidAmount)}, ")
        + string.Create(En, $"refunded {Money(b.RefundedAmount)}. ")
        + (b.HoldExpiresAt is { } hold ? string.Create(En, $"Unpaid hold until {hold:yyyy-MM-dd HH:mm} UTC. ") : "")
        + string.Create(En, $"Cancelling now would give back {Money(b.RefundIfCancelledNow)}.");

    public static string Held(BookingResponse b, string? checkoutUrl) =>
        string.Create(En, $"Held booking {b.Id}: {b.TourTitle}, {b.LocalStart:ddd d MMM yyyy, HH:mm} local time, ")
        + string.Create(En, $"{b.Guests} guests ({b.Type}), total {Money(b.TotalPrice)}. ")
        + (b.HoldExpiresAt is { } hold ? string.Create(En, $"The hold expires at {hold:yyyy-MM-dd HH:mm} UTC. ") : "")
        + (checkoutUrl is null
            ? "The checkout link isn't available right now; the traveler can pay from their bookings on TourDock."
            : $"Give the traveler this link to pay (do not pay yourself): {checkoutUrl}");

    public static string CancelPreview(BookingResponse b) =>
        $"Nothing changed yet. {Booking(b)} Ask the traveler to confirm; then call cancel_booking with confirm true.";

    public static string Cancelled(CancellationResult r) => $"Cancelled. Refund: {Money(r.Refund)}. {Booking(r.Booking)}";

    public static string Agency(AgencyResponse a) => $"Agency {a.Id}: {a.Name} ({a.Status}), your role: {a.MyRole ?? "none"}.";

    public static string AgencyBooking(AgencyBookingResponse a) => $"{Booking(a.Booking)} Booked by {a.CustomerName}.";

    public static string Stats(AgencyStatsResponse s) => string.Create(En,
        $"From {s.From:yyyy-MM-dd} to {s.To:yyyy-MM-dd}: {s.Departures} departures, {s.SeatsSold} of {s.SeatsOnSale} seats sold ")
        + string.Create(En, $"({s.OccupancyPercent}% occupancy), paid {Money(s.Revenue.Paid)}, refunded {Money(s.Revenue.Refunded)}, ")
        + string.Create(En, $"net {Money(s.Revenue.Net)}. Today: {s.Today.Departures} departures, {s.Today.Guests} guests. ")
        + string.Create(En, $"Unpaid holds: {s.PendingHolds}.");

    public static string Review(AgencyReview r) => string.Create(En,
        $"Review {r.Id} on {r.TourTitle}: {r.Rating}/5 by {r.AuthorName} on {r.CreatedAt:yyyy-MM-dd}. ")
        + $"\"{r.Comment ?? "(no comment)"}\" "
        + (r.AgencyReply is { } reply ? $"Your reply: \"{reply}\"." : "No reply yet.");

    private static string Money(MoneyDto money) => string.Create(En, $"{money.Amount:0.##} {money.Currency}");
}
