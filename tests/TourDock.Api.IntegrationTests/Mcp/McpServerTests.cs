using AwesomeAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace TourDock.Api.IntegrationTests.Mcp;

/// <summary>The MCP server at /mcp, driven by the official MCP client over Streamable HTTP.</summary>
public sealed class McpServerTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_mcp_client_lists_the_read_only_tools_and_calls_them()
    {
        var tour = await new TestApi(factory.CreateClient(), Ct).CreateDepartureAsync(title: "MCP test cruise");
        await using var client = await ConnectAsync();

        var tools = await client.ListToolsAsync(cancellationToken: Ct);
        var search = await client.CallToolAsync(
            "search_tours", new Dictionary<string, object?> { ["place"] = "Split", ["guests"] = 2 }, cancellationToken: Ct);
        var details = await client.CallToolAsync(
            "get_tour", new Dictionary<string, object?> { ["tour_id"] = tour.TourId }, cancellationToken: Ct);
        var bad = await client.CallToolAsync("get_tour", new Dictionary<string, object?> { ["tour_id"] = "nope" }, cancellationToken: Ct);

        tools.Select(t => t.Name).Should().BeEquivalentTo(["search_tours", "get_tour", "get_departures"]);
        tools.Should().OnlyContain(t => t.ProtocolTool.Annotations!.ReadOnlyHint == true);
        search.IsError.Should().NotBe(true);
        Text(search).Should().Contain($"/tours/{tour.TourId}");
        Text(details).Should().Contain("MCP test cruise").And.Contain("Cancellation:");
        bad.IsError.Should().BeTrue();
        Text(bad).Should().Contain("tour_id");
    }

    [Fact]
    public async Task An_unknown_tool_is_an_error_result_not_a_crash()
    {
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync("book_everything", new Dictionary<string, object?>(), cancellationToken: Ct);

        result.IsError.Should().BeTrue();
    }

    private async Task<McpClient> ConnectAsync()
    {
        var http = factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, cancellationToken: Ct);
    }

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
}
