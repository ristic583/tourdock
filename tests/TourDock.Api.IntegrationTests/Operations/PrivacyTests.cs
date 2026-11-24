using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using TourDock.Infrastructure.Outbox;

namespace TourDock.Api.IntegrationTests.Operations;

/// <summary>Runs the flows that touch the most personal data and checks none of it ends up in the logs.</summary>
public sealed class PrivacyTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Logs_never_contain_emails_names_or_passwords()
    {
        var api = new TestApi(factory.CreateClient(), Ct);
        var email = $"privacy-{Guid.NewGuid():N}@example.com";
        const string password = "a-very-private-password-123";
        const string name = "Zdenka Privatna";
        var (status, _) = await api.SendAsync(HttpMethod.Post, "/api/v1/auth/register", new { email, password, displayName = name });
        status.Should().Be(HttpStatusCode.Accepted);
        var token = await api.LoginAsync(email, password);
        (await api.SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { email, password = "wrong-password-999" })).Status.Should().Be(HttpStatusCode.BadRequest);

        var departure = await api.CreateDepartureAsync(daysAhead: 10);
        var bookingId = (await api.BookAsync(token, departure.DepartureId, 2)).Body.GetProperty("id").GetString()!;
        var paymentId = (await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payments", null, token)).Body.GetProperty("paymentId").GetString()!;
        await SendWebhookAsync("payment.succeeded", paymentId);
        await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/cancel", new { reason = "Changed plans" }, token);
        await factory.Services.DrainOutboxAsync(Ct);
        factory.Emails.To(email).Should().NotBeEmpty("the flow really sent this person emails");

        var logs = factory.Logs.Entries.ToList();
        logs.Should().NotBeEmpty();
        logs.Should().NotContain(e => e.Contains("@example.com", StringComparison.OrdinalIgnoreCase), "email addresses are personal data");
        logs.Should().NotContain(e => e.Contains(name, StringComparison.Ordinal), "so are names");
        logs.Should().NotContain(e => e.Contains(password, StringComparison.Ordinal) || e.Contains("wrong-password-999", StringComparison.Ordinal));
        logs.Should().NotContain(e => e.Contains(token, StringComparison.Ordinal), "and tokens");
    }

    private async Task SendWebhookAsync(string type, string reference)
    {
        var body = JsonSerializer.Serialize(new { id = $"evt_{Guid.NewGuid():N}", type, data = new { reference, reason = (string?)null } });
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var v1 = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(TourDockApiFactory.WebhookSecret), Encoding.UTF8.GetBytes($"{t}.{body}")));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/payments") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("TourDock-Signature", $"t={t},v1={v1}");
        using var response = await client.SendAsync(request, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
