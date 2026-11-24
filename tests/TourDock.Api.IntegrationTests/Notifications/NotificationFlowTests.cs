using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TourDock.Domain.Bookings;
using TourDock.Infrastructure.Outbox;

namespace TourDock.Api.IntegrationTests.Notifications;

/// <summary>Booking changes → domain events → outbox → emails, through the real API and database.</summary>
public sealed class NotificationFlowTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Paying_and_cancelling_email_the_traveler_and_the_agency_once()
    {
        var api = new TestApi(factory.CreateClient(), Ct);
        var departure = await api.CreateDepartureAsync(capacity: 8, daysAhead: 10);
        var traveler = await api.RegisterUserAsync();
        var travelerEmail = (await api.SendAsync(HttpMethod.Get, "/api/v1/me", null, traveler)).Body.GetProperty("email").GetString()!;
        var bookingId = (await api.BookAsync(traveler, departure.DepartureId, 2)).Body.GetProperty("id").GetString()!;
        var reference = Booking.ReferenceFor(Guid.Parse(bookingId)); // same "TD-XXXX-XXXX" the web app shows

        // Nothing is sent for a pending hold.
        await ProcessOutboxAsync();
        BookingMails(travelerEmail).Should().BeEmpty();

        var paymentId = (await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payments", null, traveler)).Body.GetProperty("paymentId").GetString()!;
        (await SendWebhookAsync("payment.succeeded", paymentId)).Should().Be(HttpStatusCode.OK);

        // The notification was queued in the same transaction as the confirmation.
        await using (var db = factory.CreateDbContext())
        {
            (await PendingFor(db, bookingId)).Should().Be(1);
        }

        await ProcessOutboxAsync();
        var confirmed = BookingMails(travelerEmail).Should().ContainSingle().Subject;
        confirmed.Subject.Should().StartWith("Booking confirmed: Sunset Cruise on ");
        confirmed.TextBody.Should().Contain($"Reference: {reference}").And.Contain("Meeting point: Riva 1, Split");
        factory.Emails.Sent.Should().ContainSingle(m => m.Subject.StartsWith("New booking:", StringComparison.Ordinal) && m.TextBody.Contains(reference));

        (await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/cancel", new { reason = "Plans changed" }, traveler)).Status.Should().Be(HttpStatusCode.OK);
        await ProcessOutboxAsync();
        await ProcessOutboxAsync(); // a second pass must not send anything again

        var travelerMails = BookingMails(travelerEmail);
        travelerMails.Should().HaveCount(2);
        travelerMails[1].Subject.Should().Be("Booking cancelled: Sunset Cruise");
        travelerMails[1].TextBody.Should().Contain("A refund of €100.00 is on its way");
        factory.Emails.Sent.Should().ContainSingle(m => m.Subject.StartsWith("Booking cancelled by traveler:", StringComparison.Ordinal) && m.TextBody.Contains(reference));

        await using (var db = factory.CreateDbContext())
        {
            (await PendingFor(db, bookingId)).Should().Be(0);
        }
    }

    // The payload column is jsonb, so filter in memory.
    private async Task<int> PendingFor(TourDock.Infrastructure.Persistence.TourDockDbContext db, string bookingId) =>
        (await db.OutboxMessages.Where(m => m.ProcessedAt == null).ToListAsync(Ct)).Count(m => m.Payload.Contains(bookingId, StringComparison.Ordinal));

    /// <summary>The traveler's emails about bookings, leaving out the confirm-your-email message sent at sign-up.</summary>
    private List<TourDock.Application.Abstractions.EmailMessage> BookingMails(string address) =>
        [.. factory.Emails.To(address).Where(m => !m.Subject.StartsWith("Confirm your email", StringComparison.Ordinal))];

    private Task ProcessOutboxAsync() => factory.Services.DrainOutboxAsync(Ct);

    private async Task<HttpStatusCode> SendWebhookAsync(string type, string reference)
    {
        var body = JsonSerializer.Serialize(new { id = $"evt_{Guid.NewGuid():N}", type, data = new { reference, reason = (string?)null } });
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var v1 = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(TourDockApiFactory.WebhookSecret), Encoding.UTF8.GetBytes($"{t}.{body}")));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/payments") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("TourDock-Signature", $"t={t},v1={v1}");
        using var response = await client.SendAsync(request, Ct);
        return response.StatusCode;
    }
}
