using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TourDock.Api.IntegrationTests.TravelInfo;
using TourDock.Application.Abstractions;
using TourDock.Application.Assistant;
using TourDock.Application.Observability;
using TourDock.Infrastructure.Assistant;

namespace TourDock.Api.IntegrationTests.Assistant;

/// <summary>
/// The real Claude adapter (SDK, tool loop, history, citations) against a stubbed Messages API. No containers and no
/// real model: the stub answers with canned responses and records what the adapter sent.
/// </summary>
public sealed class ClaudeTravelAssistantTests
{
    private const string TourUrl = "https://app.example.com/tours/0199a1b2-0000-7000-8000-000000000001";

    private readonly StubHttpHandler _http = new();
    private readonly List<JsonObject> _sent = [];
    private readonly Queue<string> _responses = new();
    private readonly StubTools _tools = new();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public ClaudeTravelAssistantTests() => _http.Respond = async (request, ct) =>
    {
        lock (_sent)
        {
            _sent.Add(JsonNode.Parse(request.Content!.ReadAsStringAsync(ct).Result)!.AsObject());
        }

        var body = _responses.Count > 0 ? _responses.Dequeue() : throw new InvalidOperationException("No canned response left.");
        return await Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    };

    [Fact]
    public async Task Runs_the_tool_feeds_back_citable_results_and_returns_the_cited_tours()
    {
        _responses.Enqueue(Response("tool_use", """{ "type": "tool_use", "id": "toolu_1", "name": "search_tours", "input": { "description": "calm boat trip" }, "caller": { "type": "direct" } }"""));
        _responses.Enqueue(Response("end_turn", $$"""{ "type": "text", "text": "Try the Sunset Cruise.", "citations": [ { "type": "search_result_location", "source": "{{TourUrl}}", "title": "Sunset Cruise", "cited_text": "Split", "search_result_index": 0, "start_block_index": 0, "end_block_index": 0 } ] }"""));

        var turn = await Assistant().ReplyAsync(null, "Something calm on a boat?", Ct);

        turn.Text.Should().Be("Try the Sunset Cruise.");
        turn.CitedTours.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Url = TourUrl, Title = "Sunset Cruise" });
        _tools.Calls.Should().ContainSingle().Which.Should().Be("search_tours:calm boat trip");

        var second = _sent[1]["messages"]!.AsArray();
        second.Should().HaveCount(3);
        var toolUse = second[1]!["content"]![0]!.AsObject();
        toolUse["name"]!.GetValue<string>().Should().Be("search_tours");
        toolUse.ContainsKey("caller").Should().BeFalse();
        var result = second[2]!["content"]![0]!;
        result["type"]!.GetValue<string>().Should().Be("tool_result");
        result["content"]![0]!["type"]!.GetValue<string>().Should().Be("search_result");
        result["content"]![0]!["citations"]!["enabled"]!.GetValue<bool>().Should().BeTrue();
        _sent[0]["system"]![0]!["cache_control"]!["type"]!.GetValue<string>().Should().Be("ephemeral");
    }

    [Fact]
    public async Task The_stored_history_is_replayed_unchanged_on_the_next_turn()
    {
        _responses.Enqueue(Response("end_turn", """{ "type": "thinking", "thinking": "", "signature": "sig-abc" }""", """{ "type": "text", "text": "Hello!" }"""));
        _responses.Enqueue(Response("end_turn", """{ "type": "text", "text": "Sure." }"""));

        var first = await Assistant().ReplyAsync(null, "Hi", Ct);
        await Assistant().ReplyAsync(first.History, "And tomorrow?", Ct);

        var replayed = _sent[1]["messages"]!.AsArray();
        replayed.Should().HaveCount(3);
        replayed[1]!["content"]![0]!["signature"]!.GetValue<string>().Should().Be("sig-abc", "thinking blocks go back exactly as they came");
        replayed[2]!["content"]![0]!["text"]!.GetValue<string>().Should().Be("And tomorrow?");
    }

    [Theory]
    [InlineData("refusal")]
    [InlineData("max_tokens")]
    public async Task A_refusal_or_an_empty_answer_is_unavailable(string stop)
    {
        _responses.Enqueue(Response(stop, """{ "type": "thinking", "thinking": "", "signature": "s" }"""));

        var act = () => Assistant().ReplyAsync(null, "Hi", Ct);

        await act.Should().ThrowAsync<AssistantUnavailableException>();
    }

    [Fact]
    public void A_note_rides_in_the_first_search_result_because_a_tool_result_takes_search_results_only()
    {
        var result = new AgentToolResult(
            [new AgentSource(TourUrl, "Sunset Cruise", "Split."), new AgentSource(TourUrl + "2", "Blue Cave", "Split.")],
            Note: "Matching by description is unavailable.");

        var block = ClaudeHistory.ToolResult("toolu_1", result);

        var content = block["content"]!.AsArray();
        content.Should().HaveCount(2).And.OnlyContain(b => b!["type"]!.GetValue<string>() == "search_result");
        content[0]!["content"]![0]!["text"]!.GetValue<string>().Should().StartWith("Matching by description is unavailable.");
        content[1]!["content"]![0]!["text"]!.GetValue<string>().Should().Be("Split.");
    }

    [Fact]
    public async Task A_provider_outage_is_unavailable()
    {
        _http.ReturnStatus(HttpStatusCode.ServiceUnavailable);

        var act = () => Assistant().ReplyAsync(null, "Hi", Ct);

        await act.Should().ThrowAsync<AssistantUnavailableException>();
    }

    private ClaudeTravelAssistant Assistant()
    {
        var client = new AnthropicClient { ApiKey = "test-key", HttpClient = new HttpClient(_http), MaxRetries = 0 };
        var meters = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();
        return new ClaudeTravelAssistant(client, _tools, Options.Create(new AssistantOptions { ApiKey = "test-key" }), TimeProvider.System,
            new TourDockMetrics(meters), NullLogger<ClaudeTravelAssistant>.Instance);
    }

    private static string Response(string stopReason, params string[] blocks) => $$"""
        { "id": "msg_test", "type": "message", "role": "assistant", "model": "claude-haiku-5-5",
          "content": [ {{string.Join(", ", blocks)}} ], "stop_reason": "{{stopReason}}", "stop_sequence": null,
          "usage": { "input_tokens": 10, "output_tokens": 5, "cache_read_input_tokens": 0, "cache_creation_input_tokens": 0 } }
        """;

    private sealed class StubTools : IAgentTools
    {
        public List<string> Calls { get; } = [];

        public IReadOnlyList<AgentToolDefinition> Definitions { get; } =
        [
            new("search_tours", "Search tours.", JsonDocument.Parse("""{ "type": "object", "properties": { "description": { "type": "string" } } }""").RootElement),
        ];

        public IReadOnlyList<AgentToolDefinition> DefinitionsFor(IReadOnlySet<string> scopes) => Definitions;

        public Task<AgentToolResult> RunAsync(string name, JsonElement input, IReadOnlySet<string> scopes, CancellationToken cancellationToken) =>
            RunAsync(name, input, cancellationToken);

        public Task<AgentToolResult> RunAsync(string name, JsonElement input, CancellationToken cancellationToken)
        {
            Calls.Add($"{name}:{input.GetProperty("description").GetString()}");
            return Task.FromResult(new AgentToolResult([new AgentSource(TourUrl, "Sunset Cruise", "Split, Croatia. 3 hours.")]));
        }
    }
}
