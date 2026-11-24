using System.Text.Json;
using TourDock.Application.Abstractions;
using TourDock.Application.Assistant;

namespace TourDock.Api.IntegrationTests;

/// <summary>
/// Stands in for Claude: answers with a canned reply that cites the tours a test sets, and records the history it got,
/// so tests can check what the API stores and sends back. Tests never call the real model.
/// </summary>
public sealed class FakeTravelAssistant : ITravelAssistant
{
    private readonly List<(string? History, string Message)> _calls = [];

    public IReadOnlyList<AgentSource> Cites { get; set; } = [];

    /// <summary>When true, every turn fails the way an unreachable or refusing model would.</summary>
    public bool Fail { get; set; }

    public IReadOnlyList<(string? History, string Message)> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    public Task<AssistantTurn> ReplyAsync(string? history, string message, CancellationToken cancellationToken)
    {
        lock (_calls)
        {
            _calls.Add((history, message));
        }

        if (Fail)
        {
            throw new AssistantUnavailableException("Model unreachable (test).");
        }

        // The history is a JSON array of the messages so far (the real one is the Messages API conversation).
        var messages = history is null ? [] : JsonSerializer.Deserialize<List<string>>(history)!;
        messages.Add(message);
        return Task.FromResult(new AssistantTurn(JsonSerializer.Serialize(messages), $"Answer {messages.Count}", Cites, AssistantUsage.None));
    }
}
