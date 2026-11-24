using System.Globalization;
using System.Text.Json;

namespace TourDock.Application.Assistant;

/// <summary>Bad tool input. Its message goes back to the agent, so it says what to fix.</summary>
internal sealed class AgentInputException(string message) : Exception(message);

/// <summary>Reads a tool's JSON input leniently (missing or null = not given) and strictly (wrong type = a clear error).</summary>
internal readonly struct AgentInput(JsonElement input)
{
    public string? Text(string name) =>
        Get(name) is { } value
            ? value.ValueKind == JsonValueKind.String
                ? value.GetString()!.Trim() is { Length: > 0 } text ? text : null
                : throw new AgentInputException($"'{name}' must be a string.")
            : null;

    public int? Integer(string name) =>
        Get(name) is { } value
            ? value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
                ? number
                : throw new AgentInputException($"'{name}' must be a whole number.")
            : null;

    public decimal? Decimal(string name) =>
        Get(name) is { } value
            ? value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
                ? number
                : throw new AgentInputException($"'{name}' must be a number.")
            : null;

    public DateOnly? Date(string name) =>
        Text(name) is { } text
            ? DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : throw new AgentInputException($"'{name}' must be a date like 2027-06-14.")
            : null;

    public Guid RequiredGuid(string name, string from = "search_tours") =>
        Text(name) is { } text && Guid.TryParse(text, out var id)
            ? id
            : throw new AgentInputException($"'{name}' must be an id from {from}.");

    public string RequiredText(string name) => Text(name) ?? throw new AgentInputException($"'{name}' is required.");

    public DateOnly RequiredDate(string name) => Date(name) ?? throw new AgentInputException($"'{name}' is required (YYYY-MM-DD).");

    public bool Boolean(string name) =>
        Get(name) is { } value
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new AgentInputException($"'{name}' must be true or false."),
            }
            : false;

    private JsonElement? Get(string name) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;
}
