using System.Text.Json.Serialization;
using FluentValidation;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Domain.Common;

namespace TourDock.Application.Assistant;

/// <param name="ConversationId">From the previous reply; null (or an expired id) starts a new conversation.</param>
public sealed record AskAssistantCommand(Guid? ConversationId, string Message) : ICommand<AssistantReply>;

/// <param name="Tours">Tours the reply cites, with links to their pages.</param>
/// <param name="TurnsLeft">How many more messages this conversation takes.</param>
public sealed record AssistantReply(Guid ConversationId, string Reply, IReadOnlyList<CitedTour> Tours, int TurnsLeft)
{
    /// <summary>Not part of the response body; the endpoint may expose it as a header (see AssistantSettings.ExposeUsage).</summary>
    [JsonIgnore]
    public AssistantUsage? Usage { get; init; }
}

/// <param name="Details">Price, place, rating and photo as search shows them; null if the tour is no longer public.</param>
/// <param name="Departures">The next few departures with a free seat, soonest first.</param>
public sealed record CitedTour(
    Guid Id, string Title, string Url, TourSummary? Details, IReadOnlyList<NextDepartureSummary> Departures);

public static class AssistantErrors
{
    public static readonly Error Unavailable =
        Error.Unavailable("Assistant.Unavailable", "The travel assistant is unavailable right now. Search still works.");

    public static readonly Error ConversationFull = Error.TooManyRequests(
        "Assistant.ConversationFull", "This conversation is long enough. Start a new one to keep going.");

    public static readonly Error DailyLimit = Error.TooManyRequests(
        "Assistant.DailyLimit", "The assistant has answered as many questions as it can today. Please try again tomorrow.");
}

internal sealed class AskAssistantValidator : AbstractValidator<AskAssistantCommand>
{
    public const int MaxMessageLength = 500;

    public AskAssistantValidator() => RuleFor(c => c.Message).NotEmpty().MaximumLength(MaxMessageLength);
}

/// <summary>
/// Limits first (they cost nothing), then one model turn, then the conversation is saved with a fresh expiry. A failed
/// or refused turn isn't saved, so the stored history only ever holds complete, valid exchanges.
/// </summary>
internal sealed class AskAssistantHandler(
    AssistantSettings settings, IAssistantConversations conversations, ITravelAssistant assistant, ITourCatalog catalog)
    : ICommandHandler<AskAssistantCommand, AssistantReply>
{
    /// <summary>Dates a cited tour shows in the chat; the tour page has the rest.</summary>
    public const int DeparturesPerTour = 3;

    public async Task<Result<AssistantReply>> Handle(AskAssistantCommand command, CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            return AssistantErrors.Unavailable;
        }

        var existing = command.ConversationId is { } id ? await conversations.GetAsync(id, cancellationToken) : null;
        if (existing?.Turns >= settings.MaxTurnsPerConversation)
        {
            return AssistantErrors.ConversationFull;
        }

        if (!await conversations.TryCountMessageAsync(settings.MaxMessagesPerDay, cancellationToken))
        {
            return AssistantErrors.DailyLimit;
        }

        AssistantTurn turn;
        try
        {
            turn = await assistant.ReplyAsync(existing?.History, command.Message.Trim(), cancellationToken);
        }
        catch (AssistantUnavailableException)
        {
            return AssistantErrors.Unavailable;
        }

        var conversation = new AssistantConversationState(existing?.Id ?? Guid.CreateVersion7(), turn.History, (existing?.Turns ?? 0) + 1);
        await conversations.SaveAsync(conversation, cancellationToken);

        var tours = await CitedToursAsync(turn.CitedTours, cancellationToken);
        return new AssistantReply(conversation.Id, turn.Text, tours, settings.MaxTurnsPerConversation - conversation.Turns)
        {
            Usage = turn.Usage,
        };
    }

    /// <summary>The cited tours in citation order, each with what the chat shows about it (one catalog query for all).</summary>
    private async Task<List<CitedTour>> CitedToursAsync(IReadOnlyList<AgentSource> sources, CancellationToken cancellationToken)
    {
        var cited = sources.Select(s => (Id: TourId(s.Url), Source: s)).Where(c => c.Id != Guid.Empty).ToList();
        if (cited.Count == 0)
        {
            return [];
        }

        var cards = (await catalog.GetCardsAsync(cited.Select(c => c.Id).ToList(), DeparturesPerTour, cancellationToken))
            .ToDictionary(c => c.Tour.Id);
        return cited.Select(c => cards.TryGetValue(c.Id, out var card)
                ? new CitedTour(c.Id, c.Source.Title, c.Source.Url, card.Tour, card.Departures)
                : new CitedTour(c.Id, c.Source.Title, c.Source.Url, null, []))
            .ToList();
    }

    /// <summary>Tour links end in the tour's id (see <see cref="AgentTools"/>).</summary>
    private static Guid TourId(string url) => Guid.TryParse(url.TrimEnd('/').Split('/')[^1].Split('?')[0], out var id) ? id : Guid.Empty;
}
