using TourDock.Application.Assistant;

namespace TourDock.Application.Abstractions;

// The travel assistant: a chat on the site that searches tours with the agent tools and answers from what they return.

/// <summary>Limits for the assistant, from configuration. Disabled when no model provider is configured.</summary>
/// <param name="ExposeUsage">Return token usage in a response header (for the assistant eval; off in production).</param>
public sealed record AssistantSettings(
    bool Enabled, int MaxTurnsPerConversation, int MaxMessagesPerDay, TimeSpan Retention, bool ExposeUsage = false);

/// <summary>What one turn cost: model calls and tokens, summed over the tool loop.</summary>
public sealed record AssistantUsage(int ModelCalls, long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens)
{
    public static readonly AssistantUsage None = new(0, 0, 0, 0, 0);

    public AssistantUsage Add(long input, long output, long cacheRead, long cacheWrite) =>
        new(ModelCalls + 1, InputTokens + input, OutputTokens + output, CacheReadTokens + cacheRead, CacheWriteTokens + cacheWrite);
}

/// <summary>One answer, and the conversation so far in the model provider's own format (opaque to the application).</summary>
/// <param name="CitedTours">Tours the answer cites, in the order they are first cited.</param>
public sealed record AssistantTurn(string History, string Text, IReadOnlyList<AgentSource> CitedTours, AssistantUsage Usage);

/// <summary>The model provider failed or declined (outage, timeout, safety refusal); the turn is not saved.</summary>
public sealed class AssistantUnavailableException(string reason, Exception? inner = null) : Exception(reason, inner);

public interface ITravelAssistant
{
    /// <param name="history">What <see cref="AssistantTurn.History"/> returned last time; null for a new conversation.</param>
    Task<AssistantTurn> ReplyAsync(string? history, string message, CancellationToken cancellationToken);
}

/// <summary>A stored conversation. Kept only for <see cref="AssistantSettings.Retention"/> after its last message.</summary>
public sealed record AssistantConversationState(Guid Id, string History, int Turns);

public interface IAssistantConversations
{
    /// <summary>The conversation, or null when it doesn't exist or has expired.</summary>
    Task<AssistantConversationState?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task SaveAsync(AssistantConversationState conversation, CancellationToken cancellationToken);

    /// <summary>Counts one message against today's limit (atomically); false when the limit is already reached.</summary>
    Task<bool> TryCountMessageAsync(int dailyLimit, CancellationToken cancellationToken);
}
