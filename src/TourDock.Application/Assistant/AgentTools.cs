using System.Text.Json;
using TourDock.Application.Abstractions;
using TourDock.Domain.Common;

namespace TourDock.Application.Assistant;

/// <summary>A set of tools that belong together (catalog, traveler, agency).</summary>
internal interface IAgentToolGroup
{
    IReadOnlyList<AgentToolDefinition> Definitions { get; }

    Task<AgentToolResult> RunAsync(string name, AgentInput input, CancellationToken cancellationToken);
}

/// <summary>
/// All agent tools behind one door. Scopes are checked here, once: an account tool runs only for a signed-in user who
/// granted its scope, and then as that user, so the handlers' own ownership and agency-access checks apply on top.
/// </summary>
internal sealed class AgentTools(IEnumerable<IAgentToolGroup> groups, IUserContext user) : IAgentTools
{
    private static readonly IReadOnlySet<string> NoScopes = new HashSet<string>();

    private readonly IReadOnlyList<(AgentToolDefinition Definition, IAgentToolGroup Group)> _tools =
        groups.SelectMany(g => g.Definitions.Select(d => (d, g))).ToList();

    public IReadOnlyList<AgentToolDefinition> Definitions => DefinitionsFor(NoScopes);

    public IReadOnlyList<AgentToolDefinition> DefinitionsFor(IReadOnlySet<string> scopes) =>
        _tools.Select(t => t.Definition).Where(d => d.Scope is null || scopes.Contains(d.Scope)).ToList();

    public Task<AgentToolResult> RunAsync(string name, JsonElement input, CancellationToken cancellationToken) =>
        RunAsync(name, input, NoScopes, cancellationToken);

    public async Task<AgentToolResult> RunAsync(
        string name, JsonElement input, IReadOnlySet<string> scopes, CancellationToken cancellationToken)
    {
        var tool = _tools.FirstOrDefault(t => t.Definition.Name == name);
        if (tool.Definition is null)
        {
            return AgentToolResult.Failure($"Unknown tool '{name}'.");
        }

        if (tool.Definition.Scope is { } scope && (!scopes.Contains(scope) || !user.IsAuthenticated))
        {
            return AgentToolResult.Failure(
                $"This tool needs the traveler's permission '{scope}'. Ask them to connect TourDock again and allow it.");
        }

        try
        {
            return await tool.Group.RunAsync(name, new AgentInput(input), cancellationToken);
        }
        catch (AgentInputException ex)
        {
            return AgentToolResult.Failure(ex.Message);
        }
    }
}

/// <summary>Turns an application error into a message the agent can act on.</summary>
internal static class AgentErrors
{
    public static AgentToolResult From(Error error) => AgentToolResult.Failure(error is ValidationError validation
        ? "Invalid input: " + string.Join("; ", validation.Errors.Select(e => $"{e.Key}: {string.Join(" ", e.Value)}"))
        : error.Message);
}
