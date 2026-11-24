using TourDock.Api.Infrastructure;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.TravelInfo;

namespace TourDock.Api.Endpoints;

/// <summary>Display-only data from third parties. Always answers 200 with an availability flag when a provider is down.</summary>
internal static class TravelInfoEndpoints
{
    public static RouteGroupBuilder MapTravelInfoEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("").WithTags("Travel info").AllowAnonymous();

        group.MapGet("/exchange-rates", async (IQueryHandler<GetExchangeRatesQuery, ExchangeRatesResponse> h, CancellationToken ct) =>
                (await h.Handle(new GetExchangeRatesQuery(), ct)).ToHttp())
            .Produces<ExchangeRatesResponse>()
            .WithSummary("Indicative ECB exchange rates from EUR for displaying prices in other currencies")
            .WithDescription("Prices are always charged in EUR. When the provider is down the last known rates are returned with isStale " +
                "= true, "
                             + "or available = false if there are none.");

        group.MapGet("/departures/{departureId:guid}/weather", async (Guid departureId,
                IQueryHandler<GetDepartureWeatherQuery, DepartureWeatherResponse> h, CancellationToken ct) =>
                (await h.Handle(new GetDepartureWeatherQuery(departureId), ct)).ToHttp())
            .Produces<DepartureWeatherResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Weather forecast at the tour location for the departure hour")
            .WithDescription("Forecasts exist up to 14 days ahead. Status tells the client what to show: Available, NotYetAvailable, " +
                "Past or Unavailable.");

        return api;
    }
}
