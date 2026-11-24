using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Agencies;
using TourDock.Application.Bookings;
using TourDock.Application.Catalog;
using TourDock.Application.Reviews;

namespace TourDock.Application.Assistant;

/// <summary>
/// What an agent may do for a signed-in agency member. The queries and commands are agency requests, so the access
/// decorator checks membership first: an agency the user doesn't work for is simply "not found".
/// </summary>
internal sealed class AgencyTools(
    IQueryHandler<GetMyAgenciesQuery, IReadOnlyList<AgencyResponse>> myAgencies,
    IQueryHandler<ListAgencyBookingsQuery, IReadOnlyList<AgencyBookingResponse>> bookings,
    IQueryHandler<GetAgencyStatsQuery, AgencyStatsResponse> stats,
    IQueryHandler<GetAgencyReviewsQuery, PagedResponse<AgencyReview>> reviews,
    ICommandHandler<ReplyToReviewCommand, AgencyReview> reply) : IAgentToolGroup
{
    public const string MyAgencies = "my_agencies";
    public const string AgencyBookings = "agency_bookings";
    public const string AgencyStats = "agency_stats";
    public const string AgencyReviews = "agency_reviews";
    public const string ReplyToReview = "reply_to_review";

    private const int MaxRangeDays = 31;

    public IReadOnlyList<AgentToolDefinition> Definitions => AccountToolSchemas.Agency;

    public async Task<AgentToolResult> RunAsync(string name, AgentInput input, CancellationToken cancellationToken) => name switch
    {
        MyAgencies => Text(await myAgencies.Handle(new GetMyAgenciesQuery(), cancellationToken),
            list => list.Count == 0 ? "You don't work for any agency." : string.Join("\n", list.Select(AccountToolText.Agency))),
        AgencyBookings => await BookingsAsync(input, cancellationToken),
        AgencyStats => await StatsAsync(input, cancellationToken),
        AgencyReviews => Text(await reviews.Handle(
                new GetAgencyReviewsQuery(AgencyId(input), input.Integer("page") ?? 1, 20), cancellationToken),
            page => page.Items.Count == 0 ? "No reviews on this page." : string.Join("\n", page.Items.Select(AccountToolText.Review))),
        ReplyToReview => Text(await reply.Handle(
                new ReplyToReviewCommand(AgencyId(input), input.RequiredGuid("review_id", "agency_reviews"), input.RequiredText("text")),
                cancellationToken),
            review => "Reply posted. " + AccountToolText.Review(review)),
        _ => AgentToolResult.Failure($"Unknown tool '{name}'."),
    };

    private async Task<AgentToolResult> BookingsAsync(AgentInput input, CancellationToken cancellationToken)
    {
        var (from, to) = Range(input);
        return Text(await bookings.Handle(new ListAgencyBookingsQuery(AgencyId(input), from, to), cancellationToken),
            list => list.Count == 0 ? "No bookings in that range." : string.Join("\n", list.Select(AccountToolText.AgencyBooking)));
    }

    private async Task<AgentToolResult> StatsAsync(AgentInput input, CancellationToken cancellationToken)
    {
        var (from, to) = Range(input);
        return Text(await stats.Handle(new GetAgencyStatsQuery(AgencyId(input), from, to), cancellationToken), AccountToolText.Stats);
    }

    private static Guid AgencyId(AgentInput input) => input.RequiredGuid("agency_id", "my_agencies");

    private static (DateOnly From, DateOnly To) Range(AgentInput input)
    {
        var from = input.RequiredDate("from");
        var to = input.RequiredDate("to");
        return to < from || to.DayNumber - from.DayNumber > MaxRangeDays
            ? throw new AgentInputException($"'to' must be on or after 'from', at most {MaxRangeDays} days later.")
            : (from, to);
    }

    private static AgentToolResult Text<T>(Domain.Common.Result<T> result, Func<T, string> format) =>
        result.IsFailure ? AgentErrors.From(result.Error!) : AccountToolText.Result(format(result.Value));
}
