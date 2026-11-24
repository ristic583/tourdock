using System.Globalization;
using TourDock.Api.Infrastructure;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Assistant;

namespace TourDock.Api.Endpoints;

/// <summary>
/// The travel assistant. Anonymous like search, so its cost is capped three ways: per client (rate limit), per
/// conversation (turns) and per day (all visitors).
/// </summary>
internal static class AssistantEndpoints
{
    public static RouteGroupBuilder MapAssistantEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/assistant").WithTags("Assistant").AllowAnonymous();

        group.MapGet("/", (AssistantSettings settings) => TypedResults.Ok(new AssistantStatus(settings.Enabled)))
            .WithSummary("Whether the travel assistant is available (the web shows the chat only when it is)");

        group.MapPost("/messages", async (AssistantMessageRequest b, ICommandHandler<AskAssistantCommand, AssistantReply> h,
                AssistantSettings settings, HttpContext http, CancellationToken ct) =>
            {
                var result = await h.Handle(new AskAssistantCommand(b.ConversationId, b.Message), ct);
                if (settings.ExposeUsage && result.IsSuccess && result.Value.Usage is { } u)
                {
                    http.Response.Headers["X-Assistant-Usage"] = string.Create(CultureInfo.InvariantCulture,
                        $"model_calls={u.ModelCalls};input={u.InputTokens};output={u.OutputTokens};")
                        + string.Create(CultureInfo.InvariantCulture, $"cache_read={u.CacheReadTokens};cache_write={u.CacheWriteTokens}");
                }

                return result.ToHttp();
            })
            .RequireRateLimiting(Hardening.AssistantPolicy)
            .Produces<AssistantReply>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Ask the travel assistant; it searches tours and answers with links to the tours it cites")
            .WithDescription("Send the conversationId from the previous reply to continue a conversation (kept for 24 hours). An unknown "
                             + "or expired id starts a new one. 429: the conversation or today's capacity is used up. 503: the model "
                             + "provider is unavailable or not configured.");

        return api;
    }

    internal sealed record AssistantStatus(bool Enabled);

    internal sealed record AssistantMessageRequest(Guid? ConversationId, string Message);
}
