using System.ComponentModel.DataAnnotations;

namespace TourDock.Infrastructure.Assistant;

/// <summary>The travel assistant (Claude). Without an API key it is off and the site hides the chat.</summary>
public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    public string? ApiKey { get; init; }

    /// <summary>Kept only if it passes the assistant eval (tests/TourDock.AssistantEval) against a larger model.</summary>
    [Required]
    public string Model { get; init; } = "claude-haiku-5-5";

    /// <summary>How hard the model thinks: low, medium, high or max. Chat about tours does well at low.</summary>
    [RegularExpression("^(low|medium|high|max)$")]
    public string Effort { get; init; } = "low";

    /// <summary>Room for thinking and the answer in one response; answers are a few short paragraphs.</summary>
    [Range(512, 16000)]
    public int MaxTokens { get; init; } = 2048;

    /// <summary>Model calls per message: tool rounds plus the final answer (the last call may not use tools).</summary>
    [Range(1, 8)]
    public int MaxModelCalls { get; init; } = 4;

    [Range(1, 200)]
    public int MaxTurnsPerConversation { get; init; } = 20;

    /// <summary>Across all visitors: a hard cap on spend, whatever the rate limits let through.</summary>
    [Range(1, 100_000)]
    public int MaxMessagesPerDay { get; init; } = 300;

    /// <summary>How long a conversation is kept after its last message.</summary>
    [Range(typeof(TimeSpan), "00:05:00", "7.00:00:00")]
    public TimeSpan Retention { get; init; } = TimeSpan.FromHours(24);

    /// <summary>One model call; the whole message also has to fit the API's 30-second request timeout.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "00:02:00")]
    public TimeSpan CallTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Adds an X-Assistant-Usage header with token counts, for the eval. Keep off in production.</summary>
    public bool ExposeUsage { get; init; }
}
