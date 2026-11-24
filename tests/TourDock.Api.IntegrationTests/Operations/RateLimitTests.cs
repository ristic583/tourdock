using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace TourDock.Api.IntegrationTests.Operations;

/// <summary>
/// Low limits, and a test-only way to pick the network peer's address (the in-memory server has none), so the
/// "trust X-Forwarded-For only from the web container" rule can be exercised.
/// </summary>
public sealed class RateLimitedApiFactory : TourDockApiFactory
{
    public const string WebContainerIp = "172.30.240.10";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimiting:AuthPermitPerMinute", "3");
        builder.UseSetting("RateLimiting:WritesPerMinute", "2");
        builder.UseSetting("ForwardedHeaders:KnownProxies:0", WebContainerIp);
    }
}

public sealed class RateLimitTests(RateLimitedApiFactory factory) : IClassFixture<RateLimitedApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Visitors_behind_the_web_app_get_their_own_login_budget()
    {
        // Through the web container: each visitor IP is counted on its own.
        for (var i = 0; i < 3; i++)
        {
            (await LoginAsync(peer: RateLimitedApiFactory.WebContainerIp, forwardedFor: "203.0.113.1")).Should().Be(HttpStatusCode.BadRequest, "wrong password");
        }

        using var limited = await LoginResponseAsync(RateLimitedApiFactory.WebContainerIp, "203.0.113.1");
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.Should().NotBeNull();
        (await limited.Content.ReadAsStringAsync(Ct)).Should().Contain("RateLimit.Exceeded");

        (await LoginAsync(RateLimitedApiFactory.WebContainerIp, "203.0.113.2")).Should().Be(HttpStatusCode.BadRequest, "another visitor isn't locked out, just told the password is wrong");
    }

    [Fact]
    public async Task Anyone_else_cannot_dodge_the_limit_by_faking_the_header()
    {
        for (var i = 0; i < 3; i++)
        {
            (await LoginAsync(peer: "198.51.100.9", forwardedFor: $"192.0.2.{i}")).Should().Be(HttpStatusCode.BadRequest);
        }

        (await LoginAsync("198.51.100.9", "192.0.2.99")).Should().Be(HttpStatusCode.TooManyRequests, "the header is ignored from untrusted peers");
    }

    [Fact]
    public async Task Bookings_are_limited_per_user()
    {
        // Each phase from its own address, so the 3-per-minute sign-in limit doesn't get in the way.
        var departure = await Api("198.51.100.21").CreateDepartureAsync(capacity: 8, daysAhead: 10);
        var api = Api("198.51.100.22");
        var traveler = await api.RegisterUserAsync();
        var other = await Api("198.51.100.23").RegisterUserAsync();

        (await api.BookAsync(traveler, departure.DepartureId, 1)).Status.Should().Be(HttpStatusCode.Created);
        (await api.BookAsync(traveler, departure.DepartureId, 1)).Status.Should().Be(HttpStatusCode.Created);
        (await api.BookAsync(traveler, departure.DepartureId, 1)).Status.Should().Be(HttpStatusCode.TooManyRequests);
        (await api.BookAsync(other, departure.DepartureId, 1)).Status.Should().Be(HttpStatusCode.Created, "the limit is per user, not per address");
    }

    private TestApi Api(string peer)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(RateLimitedApiFactory.PeerHeader, peer);
        return new TestApi(client, Ct);
    }

    private async Task<HttpStatusCode> LoginAsync(string peer, string forwardedFor)
    {
        using var response = await LoginResponseAsync(peer, forwardedFor);
        return response.StatusCode;
    }

    private async Task<HttpResponseMessage> LoginResponseAsync(string peer, string forwardedFor)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { email = "nobody@example.com", password = "not-the-password-1" }),
        };
        request.Headers.Add(RateLimitedApiFactory.PeerHeader, peer);
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return await client.SendAsync(request, Ct);
    }
}
