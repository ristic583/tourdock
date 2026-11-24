using Microsoft.EntityFrameworkCore;
using TourDock.Application.Abstractions;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Application.Photos;
using TourDock.Application.Tours;
using TourDock.Domain.Slots;
using TourDock.Domain.Tours;
using TourDock.Infrastructure.Embeddings;
using TourDock.Infrastructure.Places;

namespace TourDock.Infrastructure.Catalog;

/// <summary>Tour search: area, filters, projection and sort, each a step of its own.</summary>
internal sealed partial class TourCatalog
{
    private async Task<PagedResponse<TourSummary>> SearchUncachedAsync(
        SearchToursQuery q, SemanticQuery? semantic, CancellationToken cancellationToken)
    {
        var area = await ResolveAreaAsync(q, cancellationToken);
        if (area is null)
        {
            return new PagedResponse<TourSummary>([], q.Page, q.PageSize, 0); // a picked place that no longer exists
        }

        var departures = BookableDepartures(q);
        var tours = FilterByModeAndPrice(FilterByArea(PublishedTours().Where(t => departures.Any(s => s.TourId == t.Id)), area, q), q);
        var total = await tours.CountAsync(cancellationToken);

        var rows = Sort(Project(tours, departures, area, q, semantic), q.Sort, area.Center is not null, semantic is not null);
        var page = await rows.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(cancellationToken);
        return new PagedResponse<TourSummary>(page.Select(ToSummary).ToList(), q.Page, q.PageSize, total);
    }

    /// <summary>Where to search: a country, a radius around a place, or a prefix of the tour's city.</summary>
    private sealed record SearchArea(string? CountryCode, CityPoint? Center, string? CityPrefix);

    /// <summary>Null when a picked place (city id) doesn't exist: the search then finds nothing.</summary>
    private async Task<SearchArea?> ResolveAreaAsync(SearchToursQuery q, CancellationToken cancellationToken)
    {
        // Typed text that is a whole country name ("Serbia", "Србија") searches that country, not around a place.
        var countryCode = q.Country;
        var cityText = q.City;
        if (q.CityId is null && !string.IsNullOrWhiteSpace(cityText) && Countries.Named(cityText) is { } namedCountry)
        {
            countryCode ??= namedCountry.Code;
            cityText = null;
        }

        // A picked city, or the place the typed text most likely means, becomes the centre of a radius search over the
        // tours' meeting points. Typed text that matches no known place falls back to a prefix match on the tour's city.
        if (q.CityId is { } cityId)
        {
            return await cities.GetAsync(cityId, cancellationToken) is { } picked ? new SearchArea(countryCode, picked, null) : null;
        }

        var center = string.IsNullOrWhiteSpace(cityText) ? null : await cities.FindBestAsync(cityText, cancellationToken);
        return new SearchArea(countryCode, center, center is null && !string.IsNullOrWhiteSpace(cityText) ? cityText.Trim() : null);
    }

    private static IQueryable<Tour> FilterByArea(IQueryable<Tour> tours, SearchArea area, SearchToursQuery q)
    {
        if (area.Center is { } center)
        {
            var (latitude, longitude) = (center.Latitude, center.Longitude);
            var meters = (q.RadiusKm ?? PlaceSearch.DefaultRadiusKm) * 1000d;
            tours = tours.Where(t => GeoFunctions.WithinRadius(t.Location.Latitude, t.Location.Longitude, latitude, longitude, meters));
        }
        else if (area.CityPrefix is { } prefix)
        {
            // Backed by a trigram index on city (see migration), so ILIKE stays fast.
            tours = tours.Where(t => EF.Functions.ILike(t.Location.City, EscapeLike(prefix) + "%"));
        }

        if (!string.IsNullOrWhiteSpace(area.CountryCode))
        {
            var country = area.CountryCode.ToUpperInvariant();
            tours = tours.Where(t => t.Location.CountryCode == country);
        }

        return tours;
    }

    private static IQueryable<Tour> FilterByModeAndPrice(IQueryable<Tour> tours, SearchToursQuery q)
    {
        var privateMode = q.Mode == BookingMode.Private;
        tours = q.Mode switch
        {
            BookingMode.Shared => tours.Where(t => t.BookingMode != BookingMode.Private),
            BookingMode.Private => tours.Where(t => t.BookingMode != BookingMode.Shared),
            _ => tours,
        };

        // Price filters use the base price of the requested mode (per person by default).
        if (q.MinPrice is { } min)
        {
            tours = privateMode ? tours.Where(t => t.PrivatePrice!.Amount >= min) : tours.Where(t => t.PricePerPerson!.Amount >= min);
        }

        if (q.MaxPrice is { } max)
        {
            tours = privateMode ? tours.Where(t => t.PrivatePrice!.Amount <= max) : tours.Where(t => t.PricePerPerson!.Amount <= max);
        }

        return tours;
    }

    /// <summary>One search result row as the database computes it (a class, not an anonymous type, so it can be passed on).</summary>
    private sealed class SearchRow
    {
        public required Tour Tour { get; init; }

        public required string AgencyName { get; init; }

        public DateTimeOffset? NextDeparture { get; init; }

        public double? Rating { get; init; }

        public int ReviewCount { get; init; }

        public string? CoverKey { get; init; }

        public decimal SortPrice { get; init; }

        public double? DistanceMeters { get; init; }

        public double? SemanticDistance { get; init; }
    }

    private IQueryable<SearchRow> Project(
        IQueryable<Tour> tours, IQueryable<Slot> departures, SearchArea area, SearchToursQuery q, SemanticQuery? semantic)
    {
        var hasSemantic = semantic is not null;
        var (model, vector) = semantic is { } s ? (s.Model, s.Vector) : (string.Empty, Array.Empty<float>());
        var hasCenter = area.Center is not null;
        var (latitude, longitude) = area.Center is { } c ? (c.Latitude, c.Longitude) : (0d, 0d);
        var privateMode = q.Mode == BookingMode.Private;
        return tours.Select(t => new SearchRow
        {
            Tour = t,
            AgencyName = db.Agencies.Where(a => a.Id == t.AgencyId).Select(a => a.Name).First(),
            NextDeparture = departures.Where(s => s.TourId == t.Id).Min(s => (DateTimeOffset?)s.StartsAt),
            Rating = db.Reviews.Where(r => r.TourId == t.Id && !r.IsHidden).Average(r => (double?)r.Rating),
            ReviewCount = db.Reviews.Count(r => r.TourId == t.Id && !r.IsHidden),
            CoverKey = t.Photos.Where(p => p.IsCover).Select(p => p.StorageKey).FirstOrDefault(),
            SortPrice = privateMode
                ? t.PrivatePrice!.Amount
                : (t.PricePerPerson != null ? t.PricePerPerson.Amount : t.PrivatePrice!.Amount),
            DistanceMeters = hasCenter
                ? GeoFunctions.DistanceMeters(t.Location.Latitude, t.Location.Longitude, latitude, longitude)
                : (double?)null,
            SemanticDistance = hasSemantic ? SemanticFunctions.Distance(t.Id, model, vector) : null,
        });
    }

    private static IQueryable<SearchRow> Sort(IQueryable<SearchRow> rows, TourSort sort, bool hasCenter, bool hasSemantic) => sort switch
    {
        // Tours without an embedding yet (just published) have no distance and come after the ranked ones.
        TourSort.Relevance when hasSemantic => rows.OrderBy(r => r.SemanticDistance).ThenBy(r => r.NextDeparture).ThenBy(r => r.Tour.Id),
        TourSort.PriceAsc => rows.OrderBy(r => r.SortPrice).ThenBy(r => r.Tour.Id),
        TourSort.PriceDesc => rows.OrderByDescending(r => r.SortPrice).ThenBy(r => r.Tour.Id),
        // Bayesian average: every tour starts with PriorWeight imaginary reviews of PriorRating, so one 5-star review
        // doesn't outrank hundreds of 4.8s; with many reviews the score approaches the real average.
        TourSort.Rating => rows
            .OrderByDescending(r => ((r.Rating ?? 0) * r.ReviewCount + PriorRating * PriorWeight) / (r.ReviewCount + PriorWeight))
            .ThenByDescending(r => r.ReviewCount).ThenBy(r => r.Tour.Id),
        TourSort.Nearest when hasCenter => rows.OrderBy(r => r.DistanceMeters).ThenBy(r => r.NextDeparture).ThenBy(r => r.Tour.Id),
        _ => rows.OrderBy(r => r.NextDeparture).ThenBy(r => r.Tour.Id),
    };

    private TourSummary ToSummary(SearchRow r) => new(
        r.Tour.Id, r.Tour.Title, r.Tour.Location.City, r.Tour.Location.CountryCode, r.Tour.Location.TimeZoneId, r.AgencyName,
        (int)r.Tour.Duration.TotalMinutes, r.Tour.MaxGuests, r.Tour.BookingMode, r.Tour.PricePerPerson.ToDto(), r.Tour.PrivatePrice.ToDto(),
        r.NextDeparture, NextDeparture: null, r.Rating is null ? null : Math.Round(r.Rating.Value, 1), r.ReviewCount,
        r.CoverKey is null ? null : photoUrls.For(r.CoverKey).Medium,
        r.DistanceMeters is null ? null : Math.Round(r.DistanceMeters.Value / 1000, 1));
}
