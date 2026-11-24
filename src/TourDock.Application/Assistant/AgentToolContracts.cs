using System.Text.Json;

namespace TourDock.Application.Assistant;

// Tools an AI agent can use on TourDock. Written once here and offered two ways: to the travel assistant on the site
// (Claude tool use, with citations) and to any MCP client (the MCP server in the API). Catalog tools are public and
// read-only; account tools act for a signed-in user and need the scope the user granted to the agent.

/// <summary>What a user can let an agent do (OAuth scopes on the MCP server).</summary>
public static class AgentScopes
{
    /// <summary>See your bookings, start new ones (you still pay on TourDock) and cancel them.</summary>
    public const string Bookings = "bookings";

    /// <summary>See your agency's bookings, stats and reviews.</summary>
    public const string AgencyRead = "agency:read";

    /// <summary>Reply to reviews of your agency's tours.</summary>
    public const string AgencyWrite = "agency:write";

    public static readonly IReadOnlyList<string> All = [Bookings, AgencyRead, AgencyWrite];
}

/// <summary>Something the agent can cite: a tour page, with the facts the tool found about it.</summary>
public sealed record AgentSource(string Url, string Title, string Text);

/// <summary>What a tool call returned: sources to answer from, or an error the agent should read and recover from.</summary>
/// <param name="Note">Something the agent should know about the whole result (not a source to cite); the whole result when
/// there are no sources (account tools answer in plain text).</param>
public sealed record AgentToolResult(IReadOnlyList<AgentSource> Sources, string? Error = null, string? Note = null)
{
    public bool IsError => Error is not null;

    public static AgentToolResult Failure(string error) => new([], error);

    /// <summary>The same result as plain text, for clients without citations (MCP).</summary>
    public string ToText()
    {
        if (Error is not null)
        {
            return Error;
        }

        if (Sources.Count == 0)
        {
            return Note ?? "Nothing found.";
        }

        var body = string.Join("\n\n---\n\n", Sources.Select(s => $"{s.Title}\n{s.Url}\n{s.Text}"));
        return Note is null ? body : $"{Note}\n\n{body}";
    }
}

/// <summary>A tool as the model sees it: a name, when to use it, and a JSON Schema for its input.</summary>
/// <param name="Scope">Null for public tools; otherwise the scope a signed-in user must have granted.</param>
/// <param name="Destructive">Changes something that can't simply be undone (cancelling a booking).</param>
public sealed record AgentToolDefinition(
    string Name, string Description, JsonElement InputSchema, string? Scope = null, bool Destructive = false);

public interface IAgentTools
{
    /// <summary>The public tools (catalog), for the anonymous assistant and the public MCP endpoint.</summary>
    IReadOnlyList<AgentToolDefinition> Definitions { get; }

    /// <summary>Public tools plus the account tools the granted scopes allow.</summary>
    IReadOnlyList<AgentToolDefinition> DefinitionsFor(IReadOnlySet<string> scopes);

    /// <summary>
    /// Runs a public tool. Bad input and unknown tools come back as <see cref="AgentToolResult.Error"/>, never as exceptions.
    /// </summary>
    Task<AgentToolResult> RunAsync(string name, JsonElement input, CancellationToken cancellationToken);

    /// <summary>Runs any tool the granted scopes allow, for the signed-in user of the current request.</summary>
    Task<AgentToolResult> RunAsync(string name, JsonElement input, IReadOnlySet<string> scopes, CancellationToken cancellationToken);
}
