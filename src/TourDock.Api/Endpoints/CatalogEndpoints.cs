using TourDock.Api.Infrastructure;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Domain.Tours;

namespace TourDock.Api.Endpoints;

/// <summary>Public catalog for travelers. No authentication required.</summary>
internal static class CatalogEndpoints
{
    public static RouteGroupBuilder MapCatalogEndpoints(this RouteGroupBuilder api)
    {
        var tours = api.MapGroup("/tours").WithTags("Catalog").AllowAnonymous();

        tours.MapGet("/", async ([AsParameters] SearchParameters p, IQueryHandler<SearchToursQuery, TourSearchResponse> h,
            CancellationToken ct) =>
                (await h.Handle(new SearchToursQuery(
                    p.City, p.Country, p.Date, p.Guests ?? 1, p.MinPrice, p.MaxPrice, p.Mode, p.Sort ?? DefaultSort(p.Q), p.Page ?? 1,
                        p.PageSize ?? 20,
                    p.CityId, p.RadiusKm, p.Q), ct))
                .ToHttp())
            .Produces<TourSearchResponse>()
            .ProducesValidationProblem()
            .WithSummary("Search published tours with an available departure in the next 365 days")
            .WithDescription("Each result carries its next bookable departure for the searched date and group, with live seat counts. "
                             + "cityId (from GET /cities), or a city name that matches a known place, searches within radiusKm (default " +
                                 "25) "
                             + "of that place and fills distanceKm; sort=Nearest orders by it. country (ISO code, e.g. RS) limits to one " +
                                 "country; "
                             + "a city value that is a whole country name (\"Serbia\") searches that country. "
                             + "q (free text, e.g. \"calm boat trip for a family\") orders the matching tours by meaning (sort=Relevance, "
                             + "the default with q); semantic=false means it was unavailable and the normal order was used.");

        api.MapGet("/cities", async (string q, int? limit, IQueryHandler<SearchCitiesQuery, IReadOnlyList<CitySuggestion>> h,
            CancellationToken ct) =>
                (await h.Handle(new SearchCitiesQuery(q, limit ?? 8), ct)).ToHttp())
            .WithTags("Catalog")
            .AllowAnonymous()
            .Produces<IReadOnlyList<CitySuggestion>>()
            .ProducesValidationProblem()
            .WithSummary("Places for the search box: names starting with q, ignoring case and accents")
            .WithDescription("Places with published tours within 25 km come first (toursNearby), then larger places.");

        api.MapGet("/places", async (string q, int? limit, IQueryHandler<SearchPlacesQuery, PlaceSuggestions> h, CancellationToken ct) =>
                (await h.Handle(new SearchPlacesQuery(q, limit ?? 8), ct)).ToHttp())
            .WithTags("Catalog")
            .AllowAnonymous()
            .Produces<PlaceSuggestions>()
            .ProducesValidationProblem()
            .WithSummary("Search-box suggestions: countries with their top places, and places starting with q")
            .WithDescription("A country matches by its English or local name (\"ser\", \"Srbija\", \"Србија\") and lists up to six of its "
                             + "places with the most tours nearby. Search a country with GET /tours?country=RS. "
                             + "cities is the same list as GET /cities, without places already nested under a country.");

        tours.MapGet("/{tourId:guid}", async (Guid tourId, string? currency, IQueryHandler<GetPublicTourQuery, PublicTourResponse> h,
            CancellationToken ct) =>
                (await h.Handle(new GetPublicTourQuery(tourId,
                    string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant()), ct)).ToHttp())
            .Produces<PublicTourResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Public tour details")
            .WithDescription("Pass ?currency=USD (or another supported currency) to add approximate convertedPrices. "
                             + "Bookings are always charged in EUR; convertedPrices is null when no rates are available.");

        tours.MapGet("/{tourId:guid}/departures", async (Guid tourId, DateOnly? from, DateOnly? to, int? guests,
                IQueryHandler<GetDeparturesQuery, IReadOnlyList<DepartureResponse>> h, TimeProvider time, CancellationToken ct) =>
            {
                var start = from ?? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
                return (await h.Handle(new GetDeparturesQuery(tourId, start, to ?? start.AddDays(30), guests ?? 1), ct)).ToHttp();
            })
            .Produces<IReadOnlyList<DepartureResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Bookable departures with seats left and the price for each date");

        return api;
    }

    internal sealed record SearchParameters(
        string? City,
        string? Country,
        DateOnly? Date,
        int? Guests,
        decimal? MinPrice,
        decimal? MaxPrice,
        BookingMode? Mode,
        TourSort? Sort,
        int? Page,
        int? PageSize,
        Guid? CityId,
        int? RadiusKm,
        string? Q);

    private static TourSort DefaultSort(string? q) => string.IsNullOrWhiteSpace(q) ? TourSort.Soonest : TourSort.Relevance;
}
