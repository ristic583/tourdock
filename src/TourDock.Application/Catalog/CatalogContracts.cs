using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Common;
using TourDock.Application.Tours;
using TourDock.Application.TravelInfo;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Catalog;

public enum TourSort
{
    /// <summary>Earliest matching departure first.</summary>
    Soonest,
    PriceAsc,
    PriceDesc,
    Rating,

    /// <summary>Closest meeting point first; needs a place to measure from.</summary>
    Nearest,

    /// <summary>Closest in meaning to the free text (q) first; the default when q is given.</summary>
    Relevance,
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>A page of search results.</summary>
/// <param name="Semantic">
/// Null without free text (q). True when q ordered the results by meaning; false when semantic search was unavailable
/// and the results are in the normal order.
/// </param>
public sealed record TourSearchResponse(IReadOnlyList<TourSummary> Items, int Page, int PageSize, int TotalCount, bool? Semantic)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>A search text's embedding and the model it came from (stored tour vectors are compared only within one model).</summary>
public sealed record SemanticQuery(string Model, float[] Vector);

/// <param name="NextDepartureAt">Start of <paramref name="NextDeparture"/> (kept for older clients).</param>
/// <param name="NextDeparture">
/// The soonest departure that can take the searched group (date, guests, mode), with live seat counts.
/// Null when it sold out since the search was cached.
/// </param>
/// <param name="TimeZoneId">IANA time zone of the tour's location, e.g. "Europe/Zagreb".</param>
public sealed record TourSummary(
    Guid Id,
    string Title,
    string City,
    string CountryCode,
    string TimeZoneId,
    string AgencyName,
    int DurationMinutes,
    int MaxGuests,
    BookingMode BookingMode,
    MoneyDto? PricePerPerson,
    MoneyDto? PrivatePrice,
    DateTimeOffset? NextDepartureAt,
    NextDepartureSummary? NextDeparture,
    double? AverageRating,
    int ReviewCount,
    string? CoverPhotoUrl,
    double? DistanceKm = null);

/// <param name="LocalStart">Wall-clock start at the tour's location, e.g. "2027-06-05T09:00:00".</param>
/// <param name="RemainingSeats">Seats still free (capacity minus seats held or booked).</param>
public sealed record NextDepartureSummary(Guid Id, DateTimeOffset StartsAt, DateTime LocalStart, int RemainingSeats, int Capacity);

public sealed record PublicTourResponse(
    Guid Id,
    string Title,
    string Description,
    string AgencyName,
    LocationDto Location,
    int DurationMinutes,
    int MaxGuests,
    BookingMode BookingMode,
    MoneyDto? PricePerPerson,
    MoneyDto? PrivatePrice,
    IReadOnlyList<SeasonalPriceDto> SeasonalPrices,
    IReadOnlyList<RefundTierDto> CancellationPolicy,
    double? AverageRating,
    int ReviewCount,
    IReadOnlyList<Photos.PhotoDto> Photos,
    ConvertedPrices? ConvertedPrices = null);

/// <param name="LocalStart">Wall-clock start at the tour's location, e.g. "2027-06-05T09:00:00".</param>
public sealed record DepartureResponse(
    Guid Id,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateTime LocalStart,
    int Capacity,
    int RemainingSeats,
    bool CanBookSeats,
    bool CanBookPrivately,
    MoneyDto? PricePerPerson,
    MoneyDto? PrivatePrice);

public static class CatalogErrors
{
    public static readonly Error TourNotFound = Error.NotFound("Tour.NotFound", "Tour not found.");
}

/// <summary>Read-optimized, cached queries over published tours (implemented in Infrastructure).</summary>
public interface ITourCatalog
{
    /// <param name="semantic">When set, <see cref="TourSort.Relevance"/> orders by closeness to this vector.</param>
    Task<PagedResponse<TourSummary>> SearchAsync(SearchToursQuery query, SemanticQuery? semantic, CancellationToken cancellationToken);

    Task<PublicTourResponse?> GetTourAsync(Guid tourId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DepartureResponse>?> GetDeparturesAsync(GetDeparturesQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// What a search result shows about these tours, plus up to <paramref name="departures"/> upcoming departures each
    /// with a free seat (live seat counts). Only published tours come back; the order is not defined.
    /// </summary>
    Task<IReadOnlyList<TourCard>> GetCardsAsync(IReadOnlyCollection<Guid> tourIds, int departures, CancellationToken cancellationToken);
}

/// <summary>A tour as the assistant's reply shows it: its search result and its next departures with a free seat.</summary>
public sealed record TourCard(TourSummary Tour, IReadOnlyList<NextDepartureSummary> Departures);

/// <summary>Marks commands that change what travelers can see; the catalog cache is cleared after they succeed.</summary>
public interface IInvalidatesCatalog;

/// <summary>Called after agency changes so travelers don't see stale tours or departures.</summary>
public interface ICatalogCache
{
    Task InvalidateAsync(CancellationToken cancellationToken = default);
}
