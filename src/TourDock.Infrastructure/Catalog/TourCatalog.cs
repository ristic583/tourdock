using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using TourDock.Application.Abstractions;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Application.Photos;
using TourDock.Application.Slots;
using TourDock.Application.Tours;
using TourDock.Domain.Agencies;
using TourDock.Domain.Slots;
using TourDock.Domain.Tours;
using TourDock.Infrastructure.Persistence;

namespace TourDock.Infrastructure.Catalog;

/// <summary>
/// Read side for travelers: no-tracking projections straight to DTOs, cached with HybridCache.
/// HybridCache coalesces concurrent misses for the same key, so a popular search cannot stampede the database.
/// </summary>
internal sealed partial class TourCatalog(TourDockDbContext db, HybridCache cache, TimeProvider time, IPhotoUrls photoUrls, ICityDirectory cities)
    : ITourCatalog, ICatalogCache
{
    private const double PriorRating = 4.0;
    private const double PriorWeight = 5;

    public const string CacheTag = "catalog";

    /// <summary>How far ahead travelers can see and book departures.</summary>
    public const int HorizonDays = BookingHorizon.Days;

    // Seat counts change with every booking, so keep entries short-lived; agency edits invalidate immediately.
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(30),
        LocalCacheExpiration = TimeSpan.FromSeconds(30),
    };

    private static readonly string[] Tags = [CacheTag];

    public Task InvalidateAsync(CancellationToken cancellationToken = default) => cache.RemoveByTagAsync(CacheTag, cancellationToken).AsTask();

    /// <summary>
    /// Which tours match is cached (30 s); seat counts are not. Every call reads the next bookable departure of the
    /// tours on the page straight from the database (one indexed query for at most a page of tours), so "N seats left"
    /// is never stale, and bookings don't have to clear the search cache.
    /// </summary>
    public async Task<PagedResponse<TourSummary>> SearchAsync(
        SearchToursQuery query, SemanticQuery? semantic, CancellationToken cancellationToken)
    {
        // The same text always has the same vector, so the text plus "ranked by meaning or not" identifies the result.
        var page = await cache.GetOrCreateAsync(
            $"search:{CacheKey(query)}|{(semantic is null ? "plain" : "semantic:" + semantic.Model)}",
            (this, query, semantic),
            static async (state, ct) => await state.Item1.SearchUncachedAsync(state.query, state.semantic, ct),
            CacheOptions,
            Tags,
            cancellationToken);

        return page.Items.Count == 0 ? page : page with { Items = await WithLiveSeatsAsync(page.Items, query, cancellationToken) };
    }

    public async Task<PublicTourResponse?> GetTourAsync(Guid tourId, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            $"tour:{tourId}",
            (this, tourId),
            static async (state, ct) => await state.Item1.GetTourUncachedAsync(state.tourId, ct),
            CacheOptions,
            Tags,
            cancellationToken);

    public async Task<IReadOnlyList<DepartureResponse>?> GetDeparturesAsync(GetDeparturesQuery query, CancellationToken cancellationToken)
    {
        var tour = await PublishedTours().Include(t => t.SeasonalPrices).SingleOrDefaultAsync(t => t.Id == query.TourId, cancellationToken);
        if (tour is null)
        {
            return null;
        }

        var now = time.GetUtcNow();
        var to = query.To <= Today(tour).AddDays(HorizonDays) ? query.To : Today(tour).AddDays(HorizonDays);
        var slots = await db.Slots.AsNoTracking()
            .Where(s => s.TourId == tour.Id && s.Status == SlotStatus.Open && s.StartsAt > now)
            .Where(s => s.LocalDate >= query.From && s.LocalDate <= to)
            .OrderBy(s => s.StartsAt)
            .ToListAsync(cancellationToken);

        var timeZone = tour.Location.TimeZone;
        return slots.Select(s =>
        {
            var pricing = tour.PricingOn(s.LocalDate);
            var canBookSeats = Tour.AllowsShared(s.BookingMode) && !s.IsPrivatelyBooked && s.RemainingSeats >= query.Guests;
            var canBookPrivately = Tour.AllowsPrivate(s.BookingMode) && s.ReservedSeats == 0 && s.Capacity >= query.Guests;
            return new DepartureResponse(
                s.Id, s.StartsAt, s.EndsAt, TimeZoneInfo.ConvertTime(s.StartsAt, timeZone).DateTime, s.Capacity, s.RemainingSeats,
                canBookSeats, canBookPrivately, pricing.PricePerPerson.ToDto(), pricing.PrivatePrice.ToDto());
        }).ToList();
    }

    /// <summary>
    /// Not cached: the assistant cites a handful of tours, and seat counts should be live. Two queries: the search
    /// projection for the tours, and the first few bookable departures of each (a correlated, limited subquery).
    /// </summary>
    public async Task<IReadOnlyList<TourCard>> GetCardsAsync(
        IReadOnlyCollection<Guid> tourIds, int departures, CancellationToken cancellationToken)
    {
        if (tourIds.Count == 0)
        {
            return [];
        }

        var anyone = new SearchToursQuery(null, null, null, Guests: 1, null, null, null, TourSort.Soonest, Page: 1, PageSize: 1);
        var bookable = BookableDepartures(anyone);
        var tours = PublishedTours().Where(t => tourIds.Contains(t.Id));
        var rows = await Project(tours, bookable, new SearchArea(null, null, null), anyone, null).ToListAsync(cancellationToken);
        var upcoming = await tours
            .Select(t => new
            {
                t.Id,
                Slots = bookable.Where(s => s.TourId == t.Id).OrderBy(s => s.StartsAt).ThenBy(s => s.Id).Take(departures)
                    .Select(s => new { s.Id, s.StartsAt, s.Capacity, s.ReservedSeats })
                    .ToList(),
            })
            .ToDictionaryAsync(t => t.Id, t => t.Slots, cancellationToken);

        return rows.Select(ToSummary).Select(summary =>
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(summary.TimeZoneId);
            var next = upcoming.GetValueOrDefault(summary.Id, [])
                .Select(s => new NextDepartureSummary(
                    s.Id, s.StartsAt, TimeZoneInfo.ConvertTime(s.StartsAt, timeZone).DateTime, s.Capacity - s.ReservedSeats, s.Capacity))
                .ToList();
            var first = next.FirstOrDefault();
            return new TourCard(summary with { NextDepartureAt = first?.StartsAt, NextDeparture = first }, next);
        }).ToList();
    }

    private async Task<PublicTourResponse?> GetTourUncachedAsync(Guid tourId, CancellationToken cancellationToken)
    {
        var row = await PublishedTours()
            .Include(t => t.SeasonalPrices)
            .Include(t => t.Photos)
            .AsSplitQuery()
            .Where(t => t.Id == tourId)
            .Select(t => new
            {
                Tour = t,
                AgencyName = db.Agencies.Where(a => a.Id == t.AgencyId).Select(a => a.Name).First(),
                Rating = db.Reviews.Where(r => r.TourId == t.Id && !r.IsHidden).Average(r => (double?)r.Rating),
                ReviewCount = db.Reviews.Count(r => r.TourId == t.Id && !r.IsHidden),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var tour = row.Tour.ToResponse(photoUrls);
        return new PublicTourResponse(
            tour.Id, tour.Title, tour.Description, row.AgencyName, tour.Location, tour.DurationMinutes, tour.MaxGuests, tour.BookingMode,
            tour.PricePerPerson, tour.PrivatePrice, tour.SeasonalPrices, tour.CancellationPolicy,
            row.Rating is null ? null : Math.Round(row.Rating.Value, 1), row.ReviewCount, tour.Photos);
    }

    /// <summary>Overlays the current next bookable departure (and its seats) on cached search results.</summary>
    private async Task<IReadOnlyList<TourSummary>> WithLiveSeatsAsync(IReadOnlyList<TourSummary> items, SearchToursQuery q, CancellationToken cancellationToken)
    {
        var tourIds = items.Select(i => i.Id).ToList();
        var next = await BookableDepartures(q)
            .Where(s => tourIds.Contains(s.TourId))
            .GroupBy(s => s.TourId)
            .Select(g => g.OrderBy(s => s.StartsAt).ThenBy(s => s.Id)
                .Select(s => new { s.TourId, s.Id, s.StartsAt, s.Capacity, s.ReservedSeats })
                .First())
            .ToDictionaryAsync(s => s.TourId, cancellationToken);

        return items.Select(item =>
        {
            if (!next.TryGetValue(item.Id, out var s))
            {
                return item with { NextDepartureAt = null, NextDeparture = null };
            }

            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(item.TimeZoneId);
            var departure = new NextDepartureSummary(
                s.Id, s.StartsAt, TimeZoneInfo.ConvertTime(s.StartsAt, timeZone).DateTime, s.Capacity - s.ReservedSeats, s.Capacity);
            return item with { NextDepartureAt = s.StartsAt, NextDeparture = departure };
        }).ToList();
    }

    /// <summary>Departures that could take this group: open, upcoming (within the horizon), and with room in the requested mode.</summary>
    private IQueryable<Slot> BookableDepartures(SearchToursQuery q)
    {
        var now = time.GetUtcNow();
        var horizon = now.AddDays(HorizonDays);
        var wantsShared = q.Mode != BookingMode.Private;
        var wantsPrivate = q.Mode != BookingMode.Shared;

        var departures = db.Slots.AsNoTracking()
            .Where(s => s.Status == SlotStatus.Open && s.StartsAt > now && s.StartsAt < horizon)
            .Where(s =>
                (wantsShared && s.BookingMode != BookingMode.Private && !s.IsPrivatelyBooked && s.Capacity - s.ReservedSeats >= q.Guests)
                || (wantsPrivate && s.BookingMode != BookingMode.Shared && s.ReservedSeats == 0 && s.Capacity >= q.Guests));
        return q.Date is { } date ? departures.Where(s => s.LocalDate == date) : departures;
    }

    /// <summary>Visible to travelers: published, not deleted, and the agency is active.</summary>
    private IQueryable<Tour> PublishedTours() =>
        db.Tours.AsNoTracking()
            .Where(t => t.Status == TourStatus.Published)
            .Where(t => db.Agencies.Any(a => a.Id == t.AgencyId && a.Status == AgencyStatus.Active));

    private DateOnly Today(Tour tour) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), tour.Location.TimeZone).DateTime);

    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);

    private static string CacheKey(SearchToursQuery q) => string.Join('|',
        q.City?.Trim().ToLowerInvariant(), q.Country?.ToUpperInvariant(), q.Date?.ToString("O", CultureInfo.InvariantCulture), q.Guests,
        q.MinPrice?.ToString(CultureInfo.InvariantCulture), q.MaxPrice?.ToString(CultureInfo.InvariantCulture), q.Mode, q.Sort, q.Page, q.PageSize,
        q.CityId, q.RadiusKm, q.Q is null ? null : Embeddings.QueryEmbeddings.Normalize(q.Q));
}
