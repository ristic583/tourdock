using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;

namespace TourDock.Api.IntegrationTests.Operations;

/// <summary>Small per-visitor shares of the paid features, reached through the web container like real visitors.</summary>
public sealed class SpendLimitedApiFactory : TourDockApiFactory
{
    public const string WebContainerIp = "172.30.240.10";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ForwardedHeaders:KnownProxies:0", WebContainerIp);
        builder.UseSetting("RateLimiting:AssistantPerMinute", "100");
        builder.UseSetting("RateLimiting:AssistantPerDay", "2");
        builder.UseSetting("RateLimiting:SemanticSearchPerMinute", "2");
        builder.UseSetting("RateLimiting:SemanticSearchPerDay", "100");
    }
}

/// <summary>
/// Model and embedding calls cost money, so no single visitor may use up what the day allows for everyone: the assistant
/// refuses with a clear wait, and search keeps answering without the paid ranking.
/// </summary>
public sealed class SpendLimitTests(SpendLimitedApiFactory factory) : IClassFixture<SpendLimitedApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_visitor_gets_a_daily_share_of_assistant_messages()
    {
        (await AskAsync("203.0.113.10")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AskAsync("203.0.113.10")).StatusCode.Should().Be(HttpStatusCode.OK);

        using var limited = await AskAsync("203.0.113.10");
        var problem = await limited.Content.ReadFromJsonAsync<JsonElement>(Ct);

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter!.Delta.Should().BeGreaterThan(TimeSpan.FromHours(1));
        problem.GetProperty("detail").GetString().Should().Contain("limit for today").And.Contain("hours");
        (await AskAsync("203.0.113.11")).StatusCode.Should().Be(HttpStatusCode.OK, "another visitor still has their share");
    }

    [Fact]
    public async Task Over_its_share_a_visitor_still_gets_results_in_the_normal_order()
    {
        var semantic = new List<bool?>();
        foreach (var text in new[] { "quiet boat trip", "dolphins from a boat", "hiking in the mountains" })
        {
            semantic.Add(await SearchAsync("203.0.113.20", text));
        }

        semantic.Should().Equal(true, true, false);
        (await SearchAsync("203.0.113.21", "wine tasting")).Should().BeTrue("another visitor still has their share");
        (await SearchAsync("203.0.113.20", "quiet boat trip")).Should().BeTrue("a text already embedded is free");
    }

    private async Task<HttpResponseMessage> AskAsync(string visitor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/assistant/messages")
        {
            Content = JsonContent.Create(new { message = "Any boat trips?", conversationId = (Guid?)null }),
        };
        return await SendAsVisitorAsync(request, visitor);
    }

    private async Task<bool?> SearchAsync(string visitor, string text)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tours?q={Uri.EscapeDataString(text)}");
        using var response = await SendAsVisitorAsync(request, visitor);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return body.TryGetProperty("semantic", out var flag) && flag.ValueKind != JsonValueKind.Null ? flag.GetBoolean() : null;
    }

    private async Task<HttpResponseMessage> SendAsVisitorAsync(HttpRequestMessage request, string visitor)
    {
        request.Headers.Add(TourDockApiFactory.PeerHeader, SpendLimitedApiFactory.WebContainerIp);
        request.Headers.Add("X-Forwarded-For", visitor);
        using var client = factory.CreateClient();
        return await client.SendAsync(request, Ct);
    }
}
