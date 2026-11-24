using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using TourDock.Application.Catalog;
using TourDock.Domain.Agencies;
using TourDock.Domain.Tours;
using TourDock.Infrastructure.Catalog;
using TourDock.Infrastructure.Persistence;

namespace TourDock.Infrastructure.Places;

/// <summary>
/// Autocomplete and radius-search centres over the seeded cities. Name matching is a prefix LIKE on the accent-free
/// search name or alias (btree, text_pattern_ops); "tours nearby" is an earthdistance count on the tours' GiST index.
/// </summary>
internal sealed class CityDirectory(TourDockDbContext db, HybridCache cache) : ICityDirectory
{
    // Typing produces a request per keystroke; the same prefixes repeat across visitors, so cache them briefly.
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(5),
    };

    // Tour counts change when agencies publish, which clears the catalog tag.
    private static readonly string[] Tags = [TourCatalog.CacheTag];

    public async Task<IReadOnlyList<CitySuggestion>> SearchAsync(string text, int limit, CancellationToken cancellationToken)
    {
        var prefix = PlaceSearch.Normalize(text);
        return await cache.GetOrCreateAsync(
            $"cities:{prefix}:{limit}",
            (this, prefix, limit),
            static async (state, ct) => await state.Item1.SearchUncachedAsync(state.prefix, state.limit, ct),
            CacheOptions,
            Tags,
            cancellationToken);
    }

    public Task<CityPoint?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Cities.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CityPoint(c.Id, c.Name, c.Latitude, c.Longitude))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<CityPoint?> FindBestAsync(string text, CancellationToken cancellationToken)
    {
        var name = PlaceSearch.Normalize(text);
        if (name.Length == 0)
        {
            return null;
        }

        return await db.Cities.AsNoTracking()
            .Where(c => c.SearchName.StartsWith(name) || (c.SearchAlias != null && c.SearchAlias.StartsWith(name)))
            .OrderByDescending(c => c.SearchName == name || c.SearchAlias == name) // "split" is Split, not a larger "Splitska"
            .ThenByDescending(c => c.Population)
            .Select(c => new CityPoint(c.Id, c.Name, c.Latitude, c.Longitude))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<CountryPlaces> TopInCountryAsync(string countryCode, int limit, CancellationToken cancellationToken)
    {
        var code = countryCode.ToUpperInvariant();
        return await cache.GetOrCreateAsync(
            $"country-places:{code}:{limit}",
            (this, code, limit),
            static async (state, ct) => await state.Item1.TopInCountryUncachedAsync(state.code, state.limit, ct),
            CacheOptions,
            Tags,
            cancellationToken);
    }

    private async Task<CountryPlaces> TopInCountryUncachedAsync(string code, int limit, CancellationToken cancellationToken)
    {
        var toursCount = await PublishedTours().CountAsync(t => t.Location.CountryCode == code, cancellationToken);

        // The most tours nearby first: for a country that means the destinations (Zlatibor, Kopaonik), not just the capital.
        var rows = await WithToursNearby(db.Cities.AsNoTracking().Where(c => c.CountryCode == code))
            .OrderByDescending(r => r.ToursNearby)
            .ThenByDescending(r => r.City.Population)
            .ThenBy(r => r.City.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return new CountryPlaces(toursCount, rows.Select(ToSuggestion).ToList());
    }

    private async Task<IReadOnlyList<CitySuggestion>> SearchUncachedAsync(string prefix, int limit, CancellationToken cancellationToken)
    {
        if (prefix.Length == 0)
        {
            return [];
        }

        var rows = await WithToursNearby(db.Cities.AsNoTracking()
                .Where(c => c.SearchName.StartsWith(prefix) || (c.SearchAlias != null && c.SearchAlias.StartsWith(prefix))))
            .OrderByDescending(r => r.ToursNearby > 0)
            .ThenByDescending(r => r.City.Population)
            .ThenBy(r => r.City.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(ToSuggestion).ToList();
    }

    private IQueryable<Tour> PublishedTours() =>
        db.Tours.Where(t => t.Status == TourStatus.Published && db.Agencies.Any(a => a.Id == t.AgencyId && a.Status == AgencyStatus.Active));

    private IQueryable<CityRow> WithToursNearby(IQueryable<City> places)
    {
        const double Meters = PlaceSearch.DefaultRadiusKm * 1000d;
        var tours = PublishedTours();
        return places.Select(c => new CityRow
        {
            City = c,
            ToursNearby = tours.Count(t => GeoFunctions.WithinRadius(t.Location.Latitude, t.Location.Longitude, c.Latitude, c.Longitude, Meters)),
        });
    }

    private static CitySuggestion ToSuggestion(CityRow r) => new(
        r.City.Id, r.City.Name, r.City.CountryCode, r.City.Latitude, r.City.Longitude, r.City.TimeZoneId, r.ToursNearby);

    private sealed class CityRow
    {
        public required City City { get; init; }

        public int ToursNearby { get; init; }
    }
}
