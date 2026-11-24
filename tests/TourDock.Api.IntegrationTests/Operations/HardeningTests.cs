using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AwesomeAssertions;

namespace TourDock.Api.IntegrationTests.Operations;

public sealed class HardeningTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_response_carries_the_security_headers()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/tours", Ct);

        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().Equal("no-referrer");
        response.Headers.GetValues("Content-Security-Policy").Should().Equal("default-src 'none'; frame-ancestors 'none'");
        response.Headers.CacheControl?.NoStore.Should().NotBe(true, "public catalog responses may be cached");
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse("browsers never call the API directly");
    }

    [Fact]
    public void A_deployment_refuses_to_start_with_the_public_local_default_secrets()
    {
        const string publicDefault = "local-dev-only-webhook-secret-change-me";
        using var deployed = factory.WithWebHostBuilder(b => b
            .UseSetting("Deployment:RejectLocalSecrets", "true")
            .UseSetting("Payments:WebhookSecret", publicDefault));

        var start = () => deployed.CreateClient();

        var error = start.Should().Throw<Exception>().Which;
        var messages = string.Join(" | ", Flatten(error).Select(e => e.Message));
        messages.Should().Contain("Payments:WebhookSecret").And.NotContain(publicDefault, "only the setting's name may be reported");

        static IEnumerable<Exception> Flatten(Exception e) =>
            e.InnerException is null ? [e] : new[] { e }.Concat(Flatten(e.InnerException));
    }

    [Fact]
    public async Task A_deployment_with_real_secrets_starts()
    {
        using var deployed = factory.WithWebHostBuilder(b => b.UseSetting("Deployment:RejectLocalSecrets", "true"));
        using var client = deployed.CreateClient();
        (await client.GetAsync("/health/live", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Signed_in_responses_are_never_cached()
    {
        using var client = factory.CreateClient();
        var token = await new TestApi(client, Ct).RegisterUserAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/bookings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, Ct);

        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Errors_carry_the_trace_id_from_the_incoming_trace_context()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tours/{Guid.NewGuid()}");
        request.Headers.Add("traceparent", "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        problem.GetProperty("traceId").GetString().Should().Be("4bf92f3577b34da6a3ce929d0e0e4736");
        factory.Logs.Entries.Should().Contain(e => e.Contains("4bf92f3577b34da6a3ce929d0e0e4736"), "log lines carry the same trace id");
    }

    [Fact]
    public async Task Each_request_is_logged_once_without_query_strings()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/tours?city=secret-city-name", Ct);

        factory.Logs.Entries.Should().Contain(e => e.Contains("HttpLogging") && e.Contains("/api/v1/tours") && e.Contains("200"));
        factory.Logs.Entries.Should().NotContain(e => e.Contains("secret-city-name"));
    }

}
