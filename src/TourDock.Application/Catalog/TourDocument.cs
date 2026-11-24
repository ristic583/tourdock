using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TourDock.Domain.Tours;

namespace TourDock.Application.Catalog;

/// <summary>The fields of a tour that describe what it is like, for semantic search.</summary>
public sealed record TourDocumentInput(
    string Title, string Description, string City, string CountryCode, TimeSpan Duration, BookingMode BookingMode);

/// <summary>
/// Builds the text a tour is embedded from, and a hash of it. Only what describes the experience goes in: price, dates
/// and seats change often and are filtered exactly by SQL, so they would only make the vector stale.
/// </summary>
public static class TourDocument
{
    public static string Build(TourDocumentInput tour)
    {
        var country = Countries.All.FirstOrDefault(c => c.Code == tour.CountryCode)?.Name ?? tour.CountryCode;
        var text = new StringBuilder()
            .AppendLine(tour.Title.Trim())
            .AppendLine(CultureInfo.InvariantCulture, $"Location: {tour.City.Trim()}, {country}")
            .AppendLine(CultureInfo.InvariantCulture, $"Duration: {DescribeDuration(tour.Duration)}")
            .AppendLine(DescribeMode(tour.BookingMode))
            .AppendLine()
            .Append(tour.Description.Trim());
        return text.ToString();
    }

    /// <summary>Changes when the text or the model changes, so the sync job knows which tours to embed again.</summary>
    public static string Hash(string model, string document) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{model}\n{document}")));

    private static string DescribeDuration(TimeSpan duration) => duration.TotalHours switch
    {
        < 1 => string.Create(CultureInfo.InvariantCulture, $"{duration.TotalMinutes:0} minutes"),
        < 24 => string.Create(CultureInfo.InvariantCulture, $"{duration.TotalHours:0.#} hours"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{duration.TotalDays:0.#} days"),
    };

    private static string DescribeMode(BookingMode mode) => mode switch
    {
        BookingMode.Shared => "Shared tour: travelers book seats and join a group.",
        BookingMode.Private => "Private tour: one group books the whole departure.",
        _ => "Book seats on a shared departure, or the whole departure privately.",
    };
}
