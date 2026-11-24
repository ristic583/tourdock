using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TourDock.Application.Abstractions;
using TourDock.Application.Assistant;
using TourDock.Application.Observability;

namespace TourDock.Infrastructure.Assistant;

/// <summary>
/// One assistant turn with Claude: call the model, run the tools it asks for, feed the results back as citable search
/// results, and repeat until it answers (the last allowed call may not use tools). Only complete turns come back; any
/// failure becomes <see cref="AssistantUnavailableException"/> and the caller saves nothing.
/// </summary>
internal sealed partial class ClaudeTravelAssistant(
    AnthropicClient client, IAgentTools tools, IOptions<AssistantOptions> options, TimeProvider time, TourDockMetrics metrics,
    ILogger<ClaudeTravelAssistant> logger) : ITravelAssistant
{
    private readonly AssistantOptions _options = options.Value;

    public async Task<AssistantTurn> ReplyAsync(string? history, string message, CancellationToken cancellationToken)
    {
        var conversation = ClaudeHistory.Parse(history);
        conversation.Add(ClaudeHistory.UserText(message));
        var usage = AssistantUsage.None;

        for (var call = 1; call <= _options.MaxModelCalls; call++)
        {
            var response = await CallAsync(conversation, toolsAllowed: call < _options.MaxModelCalls, cancellationToken);
            usage = usage.Add(response.Usage.InputTokens, response.Usage.OutputTokens,
                response.Usage.CacheReadInputTokens ?? 0, response.Usage.CacheCreationInputTokens ?? 0);
            var stop = response.StopReason?.Raw();
            if (stop == "refusal")
            {
                throw new AssistantUnavailableException("The model declined to answer.");
            }

            conversation.Add(ClaudeHistory.Message("assistant", response.Content.Select(ClaudeHistory.FromResponse)));
            var toolUses = response.Content.Select(b => b.TryPickToolUse(out var use) ? use : null).OfType<ToolUseBlock>().ToList();
            if (stop == "tool_use" && toolUses.Count > 0)
            {
                conversation.Add(ClaudeHistory.Message("user", await RunToolsAsync(toolUses, cancellationToken)));
                continue;
            }

            var text = string.Concat(response.Content.Select(b => b.TryPickText(out var t) ? t.Text : null)).Trim();
            if (text.Length == 0)
            {
                throw new AssistantUnavailableException($"The model stopped without an answer ({stop}).");
            }

            return new AssistantTurn(ClaudeHistory.Serialize(conversation), text, CitedTours(response), usage);
        }

        throw new AssistantUnavailableException("The model kept asking for tools.");
    }

    private async Task<Message> CallAsync(List<JsonObject> conversation, bool toolsAllowed, CancellationToken cancellationToken)
    {
        var parameters = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            Thinking = new ThinkingConfigAdaptive(),
            OutputConfig = new OutputConfig { Effort = ParseEffort(_options.Effort) },
            System = new List<TextBlockParam>
            {
                new() { Text = AssistantPrompt.Instructions, CacheControl = new CacheControlEphemeral() },
                new() { Text = string.Create(CultureInfo.InvariantCulture, $"Today is {time.GetUtcNow():dddd d MMMM yyyy} (UTC).") },
            },
            Tools = [.. tools.Definitions.Select(ToTool)],
            ToolChoice = toolsAllowed ? new ToolChoiceAuto() : new ToolChoiceNone(),
            Messages = ClaudeHistory.ToParams(conversation),
        };

        try
        {
            var response = await client.Messages.Create(parameters, cancellationToken);
            var usage = response.Usage;
            metrics.AssistantTokens(_options.Model, usage.InputTokens, usage.OutputTokens,
                usage.CacheReadInputTokens ?? 0, usage.CacheCreationInputTokens ?? 0);
            return response;
        }
        catch (Exception ex) when (ex is AnthropicException or HttpRequestException or TaskCanceledException
                                   && !cancellationToken.IsCancellationRequested)
        {
            LogCallFailed(ex.GetType().Name, ex);
            throw new AssistantUnavailableException("The model call failed.", ex);
        }
    }

    private async Task<List<JsonObject>> RunToolsAsync(List<ToolUseBlock> uses, CancellationToken cancellationToken)
    {
        // One result per call, all in one message (the API requires it, and it keeps parallel tool use working).
        var results = new List<JsonObject>(uses.Count);
        foreach (var use in uses)
        {
            var result = await tools.RunAsync(use.Name, JsonSerializer.SerializeToElement(use.Input), cancellationToken);
            results.Add(ClaudeHistory.ToolResult(use.ID, result));
        }

        return results;
    }

    /// <summary>Tours the answer cites, by page, in the order they are first cited.</summary>
    private static List<AgentSource> CitedTours(Message response) => response.Content
        .Select(b => b.TryPickText(out var text) ? text.Citations : null)
        .OfType<IReadOnlyList<TextCitation>>()
        .SelectMany(citations => citations)
        .Select(c => c.TryPickCitationsSearchResultLocation(out var location) ? location : null)
        .OfType<CitationsSearchResultLocation>()
        .DistinctBy(l => l.Source)
        .Select(l => new AgentSource(l.Source, l.Title ?? string.Empty, string.Empty))
        .ToList();

    private static ToolUnion ToTool(AgentToolDefinition definition) => new Tool
    {
        Name = definition.Name,
        Description = definition.Description,
        InputSchema = InputSchema.FromRawUnchecked(definition.InputSchema.Deserialize<Dictionary<string, JsonElement>>()!),
    };

    private static Effort ParseEffort(string effort) => effort switch
    {
        "medium" => Effort.Medium,
        "high" => Effort.High,
        "max" => Effort.Max,
        _ => Effort.Low,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Assistant model call failed ({Reason}); the turn is not saved")]
    private partial void LogCallFailed(string reason, Exception exception);
}
