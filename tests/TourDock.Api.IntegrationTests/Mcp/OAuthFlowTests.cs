using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace TourDock.Api.IntegrationTests.Mcp;

/// <summary>
/// What an MCP client does to act for a traveler: discover, register, authorize with PKCE (the consent step posted
/// from the web app with its session cookie), redeem the code, and call the account tools with the token.
/// </summary>
public sealed class OAuthFlowTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private const string Origin = "http://localhost";
    private const string Resource = Origin + "/mcp/account";
    private const string Redirect = "http://127.0.0.1:51234/callback";

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Http() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Discovery_points_from_the_mcp_endpoint_to_the_authorization_server()
    {
        using var http = Http();

        var challenge = await http.PostAsync("/mcp/account", JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" }), Ct);
        var resource = await http.GetFromJsonAsync<JsonElement>("/.well-known/oauth-protected-resource/mcp/account", Ct);
        var server = await http.GetFromJsonAsync<JsonElement>("/.well-known/oauth-authorization-server", Ct);

        challenge.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        challenge.Headers.WwwAuthenticate.ToString().Should().Contain("resource_metadata=\"http://localhost/.well-known/oauth-protected-resource/mcp/account\"");
        resource.GetProperty("resource").GetString().Should().Be(Resource);
        resource.GetProperty("authorization_servers")[0].GetString().Should().Be(Origin + "/");
        server.GetProperty("registration_endpoint").GetString().Should().Be(Origin + "/connect/register");
        server.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(m => m.GetString()).Should().Equal("S256");
    }

    [Fact]
    public async Task A_traveler_connects_an_agent_that_holds_seats_and_lists_bookings_with_the_token()
    {
        var api = new TestApi(factory.CreateClient(), Ct);
        var departure = await api.CreateDepartureAsync(title: "OAuth test cruise");
        var traveler = await api.RegisterUserAsync();

        var tokens = await ConnectAsync(traveler, "bookings");
        await using var mcp = await McpAsync(tokens.GetProperty("access_token").GetString()!);
        var tools = await mcp.ListToolsAsync(cancellationToken: Ct);
        var held = Text(await mcp.CallToolAsync("start_booking",
            new Dictionary<string, object?> { ["departure_id"] = departure.DepartureId, ["guests"] = 2, ["booking_type"] = "Shared" }, cancellationToken: Ct));
        var again = Text(await mcp.CallToolAsync("start_booking",
            new Dictionary<string, object?> { ["departure_id"] = departure.DepartureId, ["guests"] = 2, ["booking_type"] = "Shared" }, cancellationToken: Ct));
        var mine = Text(await mcp.CallToolAsync("list_my_bookings", new Dictionary<string, object?>(), cancellationToken: Ct));
        var agency = await mcp.CallToolAsync("my_agencies", new Dictionary<string, object?>(), cancellationToken: Ct);

        tools.Select(t => t.Name).Should().Contain(["search_tours", "list_my_bookings", "start_booking", "cancel_booking"]).And.NotContain("my_agencies");
        held.Should().Contain("Held booking").And.Contain("http://sandbox.test/checkout/");
        again.Should().Be(held, "the same request on the same day returns the same booking");
        mine.Should().Contain("OAuth test cruise").And.Contain("2 guests");
        agency.IsError.Should().BeTrue("the token wasn't granted agency:read");
        tokens.GetProperty("refresh_token").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Cancelling_previews_first_and_only_cancels_when_confirmed()
    {
        var api = new TestApi(factory.CreateClient(), Ct);
        var departure = await api.CreateDepartureAsync();
        var traveler = await api.RegisterUserAsync();
        var (_, booking) = await api.BookAsync(traveler, departure.DepartureId, 1);
        var bookingId = booking.GetProperty("id").GetString();

        await using var mcp = await McpAsync((await ConnectAsync(traveler, "bookings")).GetProperty("access_token").GetString()!);
        var preview = Text(await mcp.CallToolAsync("cancel_booking",
            new Dictionary<string, object?> { ["booking_id"] = bookingId, ["confirm"] = false }, cancellationToken: Ct));
        var (_, still) = await api.SendAsync(HttpMethod.Get, $"/api/v1/bookings/{bookingId}", null, traveler);
        var cancelled = Text(await mcp.CallToolAsync("cancel_booking",
            new Dictionary<string, object?> { ["booking_id"] = bookingId, ["confirm"] = true }, cancellationToken: Ct));

        preview.Should().StartWith("Nothing changed yet.");
        still.GetProperty("status").GetString().Should().Be("Pending");
        cancelled.Should().StartWith("Cancelled.");
    }

    [Fact]
    public async Task An_agency_member_connects_with_agency_scopes_and_sees_only_their_agency()
    {
        var api = new TestApi(factory.CreateClient(), Ct);
        var mine = await api.CreateDepartureAsync(title: "Agency tools tour");
        var other = await api.CreateDepartureAsync();

        var tokens = await ConnectAsync(mine.AgencyToken, "agency:read");
        await using var mcp = await McpAsync(tokens.GetProperty("access_token").GetString()!);
        var agencies = Text(await mcp.CallToolAsync("my_agencies", new Dictionary<string, object?>(), cancellationToken: Ct));
        var range = new Dictionary<string, object?>
        {
            ["from"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            ["to"] = DateTime.UtcNow.AddDays(20).ToString("yyyy-MM-dd"),
        };
        var stats = Text(await mcp.CallToolAsync("agency_stats", new Dictionary<string, object?>(range) { ["agency_id"] = mine.AgencyId }, cancellationToken: Ct));
        var foreign = await mcp.CallToolAsync("agency_stats", new Dictionary<string, object?>(range) { ["agency_id"] = other.AgencyId }, cancellationToken: Ct);
        var reply = await mcp.CallToolAsync("reply_to_review", new Dictionary<string, object?>
        {
            ["agency_id"] = mine.AgencyId,
            ["review_id"] = Guid.NewGuid().ToString(),
            ["text"] = "Thanks!",
        }, cancellationToken: Ct);

        agencies.Should().Contain(mine.AgencyId).And.NotContain(other.AgencyId);
        stats.Should().Contain("departures");
        foreign.IsError.Should().BeTrue("another agency is simply not found");
        reply.IsError.Should().BeTrue("agency:write wasn't granted");
    }

    [Fact]
    public async Task A_refresh_token_gets_a_new_access_token_and_rotates()
    {
        var traveler = await new TestApi(factory.CreateClient(), Ct).RegisterUserAsync();
        var (tokens, clientId) = await ConnectWithClientAsync(traveler, "bookings");
        using var http = Http();

        var refreshed = await http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = tokens.GetProperty("refresh_token").GetString()!,
            ["client_id"] = clientId,
            ["resource"] = Resource,
        }), Ct);

        refreshed.StatusCode.Should().Be(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync(Ct));
        var next = await refreshed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        next.GetProperty("access_token").GetString().Should().NotBe(tokens.GetProperty("access_token").GetString());
        next.GetProperty("refresh_token").GetString().Should().NotBe(tokens.GetProperty("refresh_token").GetString(), "rotated");
    }

    [Fact]
    public async Task The_web_apps_own_token_and_a_cross_site_consent_post_are_refused()
    {
        var traveler = await new TestApi(factory.CreateClient(), Ct).RegisterUserAsync();
        using var http = Http();

        using var withWebToken = new HttpRequestMessage(HttpMethod.Post, "/mcp/account")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" }),
        };
        withWebToken.Headers.Authorization = new AuthenticationHeaderValue("Bearer", traveler);
        (await http.SendAsync(withWebToken, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "no token passthrough");

        var (clientId, consent) = await StartAuthorizationAsync(http, "bookings", "verifier-" + new string('x', 40));
        var post = Consent(consent, traveler, origin: "https://evil.example.com");
        (await http.SendAsync(post, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        clientId.Should().NotBeEmpty();
    }

    /// <summary>Registers a client, authorizes with PKCE, consents as the traveler and redeems the code.</summary>
    private async Task<JsonElement> ConnectAsync(string travelerToken, string scope) => (await ConnectWithClientAsync(travelerToken, scope)).Tokens;

    private async Task<(JsonElement Tokens, string ClientId)> ConnectWithClientAsync(string travelerToken, string scope)
    {
        using var http = Http();
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var (clientId, consent) = await StartAuthorizationAsync(http, scope, verifier);

        var decided = await http.SendAsync(Consent(consent, travelerToken, Origin), Ct);
        decided.StatusCode.Should().Be(HttpStatusCode.Redirect, await decided.Content.ReadAsStringAsync(Ct));
        var callback = decided.Headers.Location!;
        callback.GetLeftPart(UriPartial.Path).Should().Be(Redirect, "the loopback port the client asked for");
        var code = QueryHelpers.ParseQuery(callback.Query)["code"].ToString();

        var token = await http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = Redirect,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier,
            ["resource"] = Resource,
        }), Ct);
        token.StatusCode.Should().Be(HttpStatusCode.OK, await token.Content.ReadAsStringAsync(Ct));
        return (await token.Content.ReadFromJsonAsync<JsonElement>(Ct), clientId);
    }

    /// <summary>Registration and the authorization request, up to the redirect to the web app's consent page.</summary>
    private async Task<(string ClientId, Uri ConsentPage)> StartAuthorizationAsync(HttpClient http, string scope, string verifier)
    {
        var registered = await http.PostAsJsonAsync("/connect/register",
            new { client_name = "Test agent", redirect_uris = new[] { "http://127.0.0.1:40000/callback" } }, Ct);
        registered.StatusCode.Should().Be(HttpStatusCode.Created);
        var clientId = (await registered.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("client_id").GetString()!;

        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var url = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = Redirect,
            ["scope"] = scope,
            ["state"] = "s1",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["resource"] = Resource,
        });
        var location = new Uri(new Uri(Origin), url);
        for (var hop = 0; hop < 3 && location.AbsolutePath == "/connect/authorize"; hop++)
        {
            var response = await http.GetAsync(location.PathAndQuery, Ct);
            response.StatusCode.Should().Be(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync(Ct));
            location = new Uri(new Uri(Origin), response.Headers.Location!);
        }

        location.AbsolutePath.Should().Be("/oauth/consent");
        return (clientId, location);
    }

    /// <summary>What the consent page posts: the parked request's id, the decision and what it showed.</summary>
    private static HttpRequestMessage Consent(Uri consentPage, string travelerToken, string origin)
    {
        var query = QueryHelpers.ParseQuery(consentPage.Query);
        var post = new HttpRequestMessage(HttpMethod.Post, "/connect/authorize")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = query["client_id"].ToString(),
                ["request_uri"] = query["request_uri"].ToString(),
                ["decision"] = "allow",
                ["shown_scope"] = query["scope"].ToString(),
                ["shown_redirect_uri"] = query["redirect_uri"].ToString(),
            }),
        };
        post.Headers.Add("Origin", origin);
        var cookie = Convert.ToBase64String(Encoding.UTF8.GetBytes(travelerToken)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        post.Headers.Add("Cookie", $"td_at={cookie}");
        return post;
    }

    private async Task<McpClient> McpAsync(string accessToken)
    {
        var http = factory.CreateClient();
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/mcp/account"),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {accessToken}" },
        }, http, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, cancellationToken: Ct);
    }

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
}
