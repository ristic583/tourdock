using System.Text.Json;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Assistant;

/// <summary>
/// The public, read-only catalog tools: search, tour details and departures. Each one calls the same query handler as the public API,
/// so validation, the published/active-agency filter and caching apply unchanged.
/// </summary>
internal sealed class CatalogTools(
    IQueryHandler<SearchToursQuery, TourSearchResponse> search,
    IQueryHandler<GetPublicTourQuery, PublicTourResponse> tour,
    IQueryHandler<GetDeparturesQuery, IReadOnlyList<DepartureResponse>> departures,
    NotificationSettings settings,
    TimeProvider time) : IAgentToolGroup
{
    public const string SearchTours = "search_tours";
    public const string GetTour = "get_tour";
    public const string GetDepartures = "get_departures";

    /// <summary>How many tours one search returns to the agent; enough to choose from, small enough to read.</summary>
    public const int SearchLimit = 6;

    public IReadOnlyList<AgentToolDefinition> Definitions => AgentToolSchemas.All;

    public async Task<AgentToolResult> RunAsync(string name, AgentInput input, CancellationToken cancellationToken) => name switch
    {
        SearchTours => await SearchAsync(input, cancellationToken),
        GetTour => await TourAsync(input, cancellationToken),
        GetDepartures => await DeparturesAsync(input, cancellationToken),
        _ => AgentToolResult.Failure($"Unknown tool '{name}'."),
    };

    private async Task<AgentToolResult> SearchAsync(AgentInput input, CancellationToken cancellationToken)
    {
        var mode = input.Text("booking_type") is { } type ? Enum.Parse<BookingMode>(type, ignoreCase: true) : (BookingMode?)null;
        var text = input.Text("description");
        var query = new SearchToursQuery(
            City: input.Text("place"), Country: input.Text("country")?.ToUpperInvariant(), Date: input.Date("date"),
            Guests: input.Integer("guests") ?? 1, MinPrice: null, MaxPrice: input.Decimal("max_price_per_person"), Mode: mode,
            Sort: string.IsNullOrWhiteSpace(text) ? TourSort.Soonest : TourSort.Relevance, Page: 1, PageSize: SearchLimit, Q: text);

        var result = await search.Handle(query, cancellationToken);
        if (result.IsFailure)
        {
            return Failed(result.Error!);
        }

        var sources = result.Value.Items.Select(t => new AgentSource(Link(t.Id), t.Title, AgentToolText.Summary(t))).ToList();
        var note = result.Value.Semantic == false
            ? "Matching by description is unavailable right now, so these tours are only filtered (place, date, group, price) and "
              + "listed by date. Read the titles: they may not fit the description. Narrowing by place or country helps."
            : null;
        return new AgentToolResult(sources, Note: note);
    }

    private async Task<AgentToolResult> TourAsync(AgentInput input, CancellationToken cancellationToken)
    {
        var tourId = input.RequiredGuid("tour_id");
        var result = await tour.Handle(new GetPublicTourQuery(tourId), cancellationToken);
        return result.IsFailure
            ? Failed(result.Error!)
            : new AgentToolResult([new AgentSource(Link(tourId), result.Value.Title, AgentToolText.Details(result.Value))]);
    }

    private async Task<AgentToolResult> DeparturesAsync(AgentInput input, CancellationToken cancellationToken)
    {
        var tourId = input.RequiredGuid("tour_id");
        var from = input.Date("from") ?? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var to = input.Date("to") ?? from.AddDays(14);
        var guests = input.Integer("guests") ?? 1;

        var details = await tour.Handle(new GetPublicTourQuery(tourId), cancellationToken);
        if (details.IsFailure)
        {
            return Failed(details.Error!);
        }

        var result = await departures.Handle(new GetDeparturesQuery(tourId, from, to, guests), cancellationToken);
        if (result.IsFailure)
        {
            return Failed(result.Error!);
        }

        var text = AgentToolText.Departures(result.Value, guests, from, to);
        return new AgentToolResult([new AgentSource(Link(tourId), $"Departures: {details.Value.Title}", text)]);
    }

    /// <summary>The tour's page on the web app (absolute when the public address is configured).</summary>
    private string Link(Guid tourId) => $"{settings.WebBaseUrl?.TrimEnd('/')}/tours/{tourId}";

    private static AgentToolResult Failed(Error error) =>
        error.Type == ErrorType.NotFound ? AgentToolResult.Failure("No published tour with that id.") : AgentErrors.From(error);
}
