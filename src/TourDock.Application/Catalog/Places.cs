using System.Text;
using FluentValidation;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Domain.Common;

namespace TourDock.Application.Catalog;

// Places travelers search from: a seeded list of cities and towns, used for autocomplete and "tours near X".

/// <param name="ToursNearby">Published tours with a meeting point within <see cref="PlaceSearch.DefaultRadiusKm"/> km.</param>
public sealed record CitySuggestion(
    Guid Id, string Name, string CountryCode, double Latitude, double Longitude, string TimeZoneId, int ToursNearby);

/// <summary>The centre of a radius search.</summary>
public sealed record CityPoint(Guid Id, string Name, double Latitude, double Longitude);

public sealed record SearchCitiesQuery(string Query, int Limit = 8) : IQuery<IReadOnlyList<CitySuggestion>>;

/// <summary>Seeded places (implemented in Infrastructure, backed by PostgreSQL).</summary>
public interface ICityDirectory
{
    /// <summary>Places whose name starts with the text, ignoring case and accents; places with tours nearby first.</summary>
    Task<IReadOnlyList<CitySuggestion>> SearchAsync(string text, int limit, CancellationToken cancellationToken);

    /// <summary>The country's published tour count and its places with the most tours nearby, then the largest.</summary>
    Task<CountryPlaces> TopInCountryAsync(string countryCode, int limit, CancellationToken cancellationToken);

    Task<CityPoint?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The place a typed name most likely means: an exact name match, else the largest place starting with it.</summary>
    Task<CityPoint?> FindBestAsync(string text, CancellationToken cancellationToken);
}

public static class PlaceSearch
{
    public const int DefaultRadiusKm = 25;
    public const int MaxRadiusKm = 100;

    /// <summary>
    /// The form names are compared in: lower case, accents removed ("Šibenik" → "sibenik", "Đakovo" → "dakovo"),
    /// single spaces. The seeded search names use the same rules, so "sib" finds Šibenik. An explicit table rather than
    /// Unicode decomposition: the API runs in invariant-globalization mode, where string.Normalize can't decompose.
    /// </summary>
    public static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSpace = true;
        foreach (var raw in value)
        {
            var c = char.ToLowerInvariant(raw);
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            lastWasSpace = false;
            if (Folds.TryGetValue(c, out var folded))
            {
                builder.Append(folded);
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static readonly Dictionary<char, string> Folds = BuildFolds();

    private static Dictionary<char, string> BuildFolds()
    {
        (string Letters, string Plain)[] groups =
        [
            ("àáâãäåāăą", "a"), ("çćĉċč", "c"), ("ďđ", "d"), ("èéêëēĕėęě", "e"), ("ĝğġģ", "g"), ("ĥħ", "h"),
            ("ìíîïĩīĭįı", "i"), ("ĵ", "j"), ("ķ", "k"), ("ĺļľŀł", "l"), ("ñńņňŉ", "n"), ("òóôõöøōŏő", "o"), ("ŕŗř", "r"),
            ("śŝşšș", "s"), ("ţťŧț", "t"), ("ùúûüũūŭůűų", "u"), ("ŵ", "w"), ("ýÿŷ", "y"), ("źżž", "z"),
            ("æ", "ae"), ("œ", "oe"), ("ß", "ss"),

            // Serbian and Macedonian Cyrillic, transliterated the way the Latin names are folded ("Ниш" finds Niš).
            ("а", "a"), ("б", "b"), ("в", "v"), ("г", "g"), ("д", "d"), ("ђ", "d"), ("ѓ", "g"), ("е", "e"), ("ж", "z"),
            ("з", "z"), ("ѕ", "dz"), ("и", "i"), ("ј", "j"), ("к", "k"), ("ќ", "k"), ("л", "l"), ("љ", "lj"), ("м", "m"),
            ("н", "n"), ("њ", "nj"), ("о", "o"), ("п", "p"), ("р", "r"), ("с", "s"), ("т", "t"), ("ћ", "c"), ("у", "u"),
            ("ф", "f"), ("х", "h"), ("ц", "c"), ("ч", "c"), ("џ", "dz"), ("ш", "s"),
        ];
        var folds = new Dictionary<char, string>();
        foreach (var (letters, plain) in groups)
        {
            foreach (var letter in letters)
            {
                folds[letter] = plain;
            }
        }

        return folds;
    }
}

internal sealed class SearchCitiesValidator : AbstractValidator<SearchCitiesQuery>
{
    public SearchCitiesValidator()
    {
        RuleFor(q => q.Query).NotEmpty().MaximumLength(100);
        RuleFor(q => q.Limit).InclusiveBetween(1, 20);
    }
}

internal sealed class SearchCitiesHandler(ICityDirectory cities) : IQueryHandler<SearchCitiesQuery, IReadOnlyList<CitySuggestion>>
{
    public async Task<Result<IReadOnlyList<CitySuggestion>>> Handle(SearchCitiesQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<CitySuggestion> found = PlaceSearch.Normalize(query.Query).Length == 0 ? [] : await cities.SearchAsync(query.Query, query.Limit, cancellationToken);
        return Result<IReadOnlyList<CitySuggestion>>.Success(found);
    }
}
