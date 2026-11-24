using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Models.Messages;
using TourDock.Application.Assistant;

namespace TourDock.Infrastructure.Assistant;

/// <summary>
/// The conversation in the Messages API's wire format, stored as JSON. Claude's own blocks (text with citations,
/// thinking with its signature, tool calls) are stored exactly as they came back and replayed unchanged, which is
/// what thinking blocks require; our blocks (the traveler's text, tool results) are built the same way every time.
/// </summary>
internal static class ClaudeHistory
{
    public static List<JsonObject> Parse(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonNode.Parse(json)!.AsArray().Select(m => m!.AsObject()).ToList();

    public static string Serialize(List<JsonObject> messages) => new JsonArray([.. messages.Select(m => m.DeepClone())]).ToJsonString();

    public static JsonObject UserText(string text) =>
        Message("user", [new JsonObject { ["type"] = "text", ["text"] = text }]);

    public static JsonObject Message(string role, IEnumerable<JsonObject> blocks) =>
        new() { ["role"] = role, ["content"] = new JsonArray([.. blocks]) };

    /// <summary>A response block as wire JSON. A tool call's "caller" is left out, as when building the params by hand.</summary>
    public static JsonObject FromResponse(ContentBlock block)
    {
        var json = JsonSerializer.SerializeToNode(block)!.AsObject();
        if ((string?)json["type"] == "tool_use")
        {
            json.Remove("caller");
        }

        return json;
    }

    /// <summary>
    /// Tool output as search results, so Claude cites each tour by its page; an error as an error result. The API takes
    /// a tool result of search results only (no text block beside them), so a note rides in the first result's text.
    /// </summary>
    public static JsonObject ToolResult(string toolUseId, AgentToolResult result)
    {
        var block = new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = toolUseId };
        if (result.IsError)
        {
            block["is_error"] = true;
            block["content"] = result.Error;
        }
        else if (result.Sources.Count == 0)
        {
            block["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = result.Note ?? "Nothing found." });
        }
        else
        {
            block["content"] = new JsonArray([.. result.Sources.Select((s, i) => new JsonObject
            {
                ["type"] = "search_result",
                ["source"] = s.Url,
                ["title"] = s.Title,
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = i == 0 && result.Note is { } note ? $"{note}\n\n{s.Text}" : s.Text,
                }),
                ["citations"] = new JsonObject { ["enabled"] = true },
            })]);
        }

        return block;
    }

    public static List<MessageParam> ToParams(IEnumerable<JsonObject> messages) => messages
        .Select(m => (string?)m["role"] == "assistant"
            ? (MessageParam)new AssistantMessageParam { Content = Blocks(m) }
            : new UserMessageParam { Content = Blocks(m) })
        .ToList();

    private static List<ContentBlockParam> Blocks(JsonObject message) =>
        message["content"]!.AsArray().Select(b => Block(b!.AsObject())).ToList();

    private static ContentBlockParam Block(JsonObject block)
    {
        var raw = block.Deserialize<Dictionary<string, JsonElement>>()!;
        return (string?)block["type"] switch
        {
            "text" => TextBlockParam.FromRawUnchecked(raw),
            "thinking" => ThinkingBlockParam.FromRawUnchecked(raw),
            "redacted_thinking" => RedactedThinkingBlockParam.FromRawUnchecked(raw),
            "tool_use" => ToolUseBlockParam.FromRawUnchecked(raw),
            "tool_result" => ToolResultBlockParam.FromRawUnchecked(raw),
            var type => throw new InvalidDataException($"Unexpected block type '{type}' in a stored conversation."),
        };
    }
}
