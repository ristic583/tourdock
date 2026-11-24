using FluentValidation;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Domain.Common;

namespace TourDock.Application.Catalog;

// Countries in the search box: "Serbia" lists the country itself and its best places underneath (Zlatibor, Kopaonik…).

/// <param name="ToursCount">Published tours of active agencies in the country.</param>
/// <param name="Places">The country's places with the most tours nearby, then the largest.</param>
public sealed record CountrySuggestion(string Code, string Name, int ToursCount, IReadOnlyList<CitySuggestion> Places);

/// <summary>Search-box suggestions: matching countries (with their top places nested) and matching places.</summary>
public sealed record PlaceSuggestions(IReadOnlyList<CountrySuggestion> Countries, IReadOnlyList<CitySuggestion> Cities);

public sealed record SearchPlacesQuery(string Query, int Limit = 8) : IQuery<PlaceSuggestions>;

public sealed record CountryPlaces(int ToursCount, IReadOnlyList<CitySuggestion> Places);

/// <summary>A country that has seeded places.</summary>
/// <param name="Aliases">Other names travelers type: the local name in Latin script (Cyrillic folds to it), short forms.</param>
public sealed record Country(string Code, string Name, params string[] Aliases);

/// <summary>
/// The countries the seeded places are in. A short static list: it changes only when a migration adds places in a new
/// country, so it lives in code next to the name folding rather than in another table.
/// </summary>
public static class Countries
{
    /// <summary>Shorter prefixes ("s") would list half the countries above the places the visitor is typing.</summary>
    public const int MinPrefixLength = 2;

    public static readonly IReadOnlyList<Country> All =
    [
        new("AL", "Albania", "Shqipëria", "Shqipëri"),
        new("BA", "Bosnia and Herzegovina", "Bosna i Hercegovina", "Bosnia", "BiH"),
        new("ES", "Spain", "España"),
        new("GR", "Greece", "Ellada", "Hellas"),
        new("HR", "Croatia", "Hrvatska"),
        new("IT", "Italy", "Italia"),
        new("ME", "Montenegro", "Crna Gora"),
        new("MK", "North Macedonia", "Severna Makedonija", "Macedonia", "Makedonija"),
        new("NO", "Norway", "Norge", "Noreg"),
        new("PT", "Portugal"),
        new("RS", "Serbia", "Srbija"),
        new("SI", "Slovenia", "Slovenija"),
    ];

    private static readonly (Country Country, string[] Names)[] Searchable =
        All.Select(c => (c, new[] { c.Name }.Concat(c.Aliases).Select(PlaceSearch.Normalize).ToArray())).ToArray();

    public static Country? ByCode(string code) =>
        All.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Countries with a name starting with the text (case, accents and Cyrillic ignored); exact names first.</summary>
    public static IReadOnlyList<Country> Matching(string text)
    {
        var prefix = PlaceSearch.Normalize(text);
        if (prefix.Length < MinPrefixLength)
        {
            return [];
        }

        return Searchable
            .Where(s => s.Names.Any(n => n.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderByDescending(s => s.Names.Contains(prefix))
            .ThenBy(s => s.Country.Name, StringComparer.Ordinal)
            .Select(s => s.Country)
            .ToList();
    }

    /// <summary>The country the whole text names ("serbia", "Србија"), for a search typed without picking a suggestion.</summary>
    public static Country? Named(string text)
    {
        var name = PlaceSearch.Normalize(text);
        return name.Length == 0 ? null : Searchable.Where(s => s.Names.Contains(name)).Select(s => s.Country).FirstOrDefault();
    }
}

internal sealed class SearchPlacesValidator : AbstractValidator<SearchPlacesQuery>
{
    public SearchPlacesValidator()
    {
        RuleFor(q => q.Query).NotEmpty().MaximumLength(100);
        RuleFor(q => q.Limit).InclusiveBetween(1, 20);
    }
}

internal sealed class SearchPlacesHandler(ICityDirectory cities) : IQueryHandler<SearchPlacesQuery, PlaceSuggestions>
{
    /// <summary>Places listed under a matched country.</summary>
    internal const int PlacesPerCountry = 6;

    /// <summary>At most this many countries, so a short prefix doesn't push the places out of view.</summary>
    internal const int MaxCountries = 2;

    public async Task<Result<PlaceSuggestions>> Handle(SearchPlacesQuery query, CancellationToken cancellationToken)
    {
        if (PlaceSearch.Normalize(query.Query).Length == 0)
        {
            return Result<PlaceSuggestions>.Success(new PlaceSuggestions([], []));
        }

        var countries = new List<CountrySuggestion>();
        foreach (var country in Countries.Matching(query.Query).Take(MaxCountries))
        {
            var places = await cities.TopInCountryAsync(country.Code, PlacesPerCountry, cancellationToken);
            countries.Add(new CountrySuggestion(country.Code, country.Name, places.ToursCount, places.Places));
        }

        // Places already nested under a country aren't repeated in the flat list.
        var nested = countries.SelectMany(c => c.Places).Select(p => p.Id).ToHashSet();
        var found = await cities.SearchAsync(query.Query, query.Limit, cancellationToken);
        return Result<PlaceSuggestions>.Success(new PlaceSuggestions(countries, found.Where(c => !nested.Contains(c.Id)).ToList()));
    }
}
