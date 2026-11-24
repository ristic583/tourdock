using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;

namespace TourDock.Api.IntegrationTests.Mcp;

/// <summary>
/// The public site is HTTPS, but TLS ends in front of the edge proxy, so the API itself is reached over plain HTTP.
/// The edge passes the visitor's scheme on (X-Forwarded-Proto) and the API trusts it only from known proxies.
/// </summary>
public sealed class EdgeProxiedApiFactory : TourDockApiFactory
{
    public const string EdgeIp = "172.30.240.11";
    public const string Origin = "https://localhost";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ForwardedHeaders:KnownProxies:0", EdgeIp);
        builder.UseSetting("OAuth:Issuer", Origin);
    }
}

public sealed class OAuthBehindEdgeTests(EdgeProxiedApiFactory factory) : IClassFixture<EdgeProxiedApiFactory>
{
    private const string Origin = EdgeProxiedApiFactory.Origin;

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Through_the_edge_discovery_and_resource_metadata_name_https_urls()
    {
        var server = await GetJsonAsync("/.well-known/oauth-authorization-server", EdgeProxiedApiFactory.EdgeIp);
        var resource = await GetJsonAsync("/.well-known/oauth-protected-resource/mcp/account", EdgeProxiedApiFactory.EdgeIp);

        server.GetProperty("authorization_endpoint").GetString().Should().Be(Origin + "/connect/authorize");
        server.GetProperty("token_endpoint").GetString().Should().Be(Origin + "/connect/token");
        server.GetProperty("jwks_uri").GetString().Should().Be(Origin + "/.well-known/jwks");
        resource.GetProperty("resource").GetString().Should().Be(Origin + "/mcp/account");
    }

    [Fact]
    public async Task A_scheme_claimed_by_anyone_else_is_ignored()
    {
        var server = await GetJsonAsync("/.well-known/oauth-authorization-server", peer: "198.51.100.9");

        server.GetProperty("authorization_endpoint").GetString().Should().Be("http://localhost/connect/authorize");
    }

    private async Task<JsonElement> GetJsonAsync(string path, string peer)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(TourDockApiFactory.PeerHeader, peer);
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await client.SendAsync(request, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, path);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }
}
