using System.Globalization;
using System.Text;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Application.Tours;
using TourDock.Domain.Tours;

namespace TourDock.Application.Assistant;

/// <summary>
/// Tool results as short, whole sentences. With citations the model quotes them, so each one has to read well on its
/// own. Ids are included so it can ask for more.
/// </summary>
internal static class AgentToolText
{
    private static readonly CultureInfo En = CultureInfo.InvariantCulture;

    public static string Summary(TourSummary t)
    {
        var text = new StringBuilder()
            .AppendLine(En, $"Tour id: {t.Id}.")
            .AppendLine(En, $"It starts in {t.City}, {Country(t.CountryCode)}{Distance(t.DistanceKm)}.")
            .AppendLine(En, $"It takes {Duration(t.DurationMinutes)}. {Mode(t.BookingMode)}")
            .AppendLine(En, $"It costs {Prices(t.PricePerPerson, t.PrivatePrice)}.")
            .AppendLine(En, $"It is run by {t.AgencyName}. {Rating(t.AverageRating, t.ReviewCount)}");
        text.Append(t.NextDeparture is { } next
            ? string.Create(En, $"The next departure for this group is on {next.LocalStart:ddd d MMM yyyy} at {next.LocalStart:HH:mm} ")
              + string.Create(En, $"local time, with {next.RemainingSeats} of {next.Capacity} seats free.")
            : "There is no departure for this group right now.");
        return text.ToString();
    }

    public static string Details(PublicTourResponse t)
    {
        var text = new StringBuilder()
            .AppendLine(En, $"Tour id: {t.Id}.")
            .AppendLine(En, $"It is run by {t.AgencyName}. {Rating(t.AverageRating, t.ReviewCount)}")
            .AppendLine(En, $"The meeting point is {t.Location.Address}, {t.Location.City}, {Country(t.Location.CountryCode)}.")
            .AppendLine(En, $"It takes {Duration(t.DurationMinutes)}, for up to {t.MaxGuests} guests. {Mode(t.BookingMode)}")
            .AppendLine(En, $"The base price is {Prices(t.PricePerPerson, t.PrivatePrice)}.");
        foreach (var season in t.SeasonalPrices)
        {
            var price = Prices(season.PricePerPerson, season.PrivatePrice);
            text.AppendLine(En, $"From {season.From:d MMM} to {season.To:d MMM} it costs {price}.");
        }

        text.AppendLine(En, $"Cancellation: {Policy(t.CancellationPolicy)}.");
        return text.AppendLine().Append(t.Description.Trim()).ToString();
    }

    public static string Departures(IReadOnlyList<DepartureResponse> departures, int guests, DateOnly from, DateOnly to)
    {
        if (departures.Count == 0)
        {
            return string.Create(En, $"No departures from {from:yyyy-MM-dd} to {to:yyyy-MM-dd} that can take {guests} guests.");
        }

        var text = new StringBuilder().AppendLine(En, $"Departures for {guests} guests, local time:");
        foreach (var d in departures.Take(20))
        {
            var ways = (d.CanBookSeats, d.CanBookPrivately) switch
            {
                (true, true) => "seats or private",
                (true, false) => "seats",
                (false, true) => "private only",
                _ => "not bookable for this group",
            };
            var seats = string.Create(En, $"{d.RemainingSeats} of {d.Capacity} seats free ({ways})");
            text.AppendLine(En, $"- {d.LocalStart:ddd d MMM yyyy, HH:mm}: {seats}; {Prices(d.PricePerPerson, d.PrivatePrice)}");
        }

        return text.ToString().TrimEnd();
    }

    private static string Country(string code) => Countries.ByCode(code)?.Name ?? code;

    private static string Distance(double? km) => km is { } value ? string.Create(En, $" ({value:0.#} km from the searched place)") : "";

    private static string Duration(int minutes) => minutes switch
    {
        < 60 => $"{minutes} minutes",
        < 24 * 60 => string.Create(En, $"{minutes / 60d:0.#} hours"),
        _ => string.Create(En, $"{minutes / (24 * 60d):0.#} days"),
    };

    private static string Mode(BookingMode mode) => mode switch
    {
        BookingMode.Shared => "Travelers book seats and join a group.",
        BookingMode.Private => "One group books the whole departure.",
        _ => "Travelers can book seats, or the whole departure privately.",
    };

    private static string Prices(MoneyDto? perPerson, MoneyDto? privatePrice) => (perPerson, privatePrice) switch
    {
        ({ } p, { } q) => $"{Money(p)} per person, or {Money(q)} for a private departure",
        ({ } p, null) => $"{Money(p)} per person",
        (null, { } q) => $"{Money(q)} for a private departure",
        _ => "on request",
    };

    private static string Money(MoneyDto money) => string.Create(En, $"{money.Amount:0.##} {money.Currency}");

    private static string Rating(double? average, int count) =>
        average is { } rating ? string.Create(En, $"It is rated {rating:0.0} from {count} reviews.") : "It has no reviews yet.";

    private static string Policy(IReadOnlyList<RefundTierDto> tiers) => tiers.Count == 0
        ? "no refunds"
        : string.Join("; ", tiers.OrderByDescending(t => t.HoursBeforeStart)
            .Select(t => string.Create(En, $"{t.RefundPercent}% back if cancelled at least {t.HoursBeforeStart} hours before")));
}
