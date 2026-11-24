using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Bookings;
using TourDock.Domain.Bookings;
using TourDock.Domain.Payments;
using TourDock.Infrastructure.Outbox;

namespace TourDock.Api.IntegrationTests;

public sealed class PaymentTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private TestApi Api() => new(factory.CreateClient(), Ct);

    [Fact]
    public async Task Paying_confirms_the_booking_and_duplicate_webhooks_are_ignored()
    {
        var (api, traveler, bookingId) = await BookAsync(guests: 2);

        var (status, payment) = await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payments", null, traveler);
        status.Should().Be(HttpStatusCode.OK);
        payment.GetProperty("checkoutUrl").GetString().Should().StartWith("http://sandbox.test/checkout/");
        var paymentId = payment.GetProperty("paymentId").GetString()!;

        var eventId = $"evt_{Guid.NewGuid():N}";
        (await SendWebhookAsync(eventId, "payment.succeeded", paymentId)).Should().Be(HttpStatusCode.OK);
        (await SendWebhookAsync(eventId, "payment.succeeded", paymentId)).Should().Be(HttpStatusCode.OK, "re-delivery is acknowledged");

        var (_, booking) = await api.SendAsync(HttpMethod.Get, $"/api/v1/bookings/{bookingId}", null, traveler);
        booking.GetProperty("status").GetString().Should().Be("Confirmed");
        booking.GetProperty("paidAmount").GetProperty("amount").GetDecimal().Should().Be(100m);
        await using var db = factory.CreateDbContext();
        (await db.ProcessedEvents.CountAsync(e => e.EventId == eventId, Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Starting_payment_twice_reuses_the_same_payment()
    {
        var (api, traveler, bookingId) = await BookAsync();

        var first = await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payments", null, traveler);
        var second = await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payments", null, traveler);

        second.Body.GetProperty("paymentId").GetString().Should().Be(first.Body.GetProperty("paymentId").GetString());
        second.Body.GetProperty("checkoutUrl").GetString().Should().Be(first.Body.GetProperty("checkoutUrl").GetString());
    }

    [Fact]
    public async Task Webhooks_with_a_bad_or_old_signature_are_rejected()
    {
        var body = JsonSerializer.Serialize(new { id = "evt_x", type = "payment.succeeded", data = new { reference = Guid.NewGuid() } });
        using var client = factory.CreateClient();

        var forged = await PostWebhookAsync(client, body, $"t={Now()},v1={new string('a', 64)}");
        var replayed = await PostWebhookAsync(client, body, Signature(body, DateTimeOffset.UtcNow.AddMinutes(-10)));
        var unsigned = await PostWebhookAsync(client, body, null);
        var repeatedKey = await PostWebhookAsync(client, body, $"t={Now()},t=1,{Signature(body, DateTimeOffset.UtcNow).Split(',')[1]}");

        forged.Should().Be(HttpStatusCode.Unauthorized);
        replayed.Should().Be(HttpStatusCode.Unauthorized);
        unsigned.Should().Be(HttpStatusCode.Unauthorized);
        repeatedKey.Should().Be(HttpStatusCode.Unauthorized, "an ambiguous header is refused, not a server error");
    }

    [Fact]
    public async Task The_same_event_delivered_several_times_at_once_is_acknowledged_every_time()
    {
        var (api, traveler, bookingId) = await BookAsync();
        var paymentId = (await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payments", null, traveler)).Body.GetProperty("paymentId").GetString()!;
        var eventId = $"evt_{Guid.NewGuid():N}";

        var codes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => SendWebhookAsync(eventId, "payment.failed", paymentId, "Card declined")));

        codes.Should().AllBeEquivalentTo(HttpStatusCode.OK, "a duplicate is not an error the provider should retry");
        await using var db = factory.CreateDbContext();
        (await db.ProcessedEvents.CountAsync(e => e.EventId == eventId, Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Failed_payment_keeps_the_booking_pending_so_the_customer_can_retry()
    {
        var (api, traveler, bookingId) = await BookAsync();
        var paymentId = (await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payments", null, traveler)).Body.GetProperty("paymentId").GetString()!;

        await SendWebhookAsync($"evt_{Guid.NewGuid():N}", "payment.failed", paymentId, reason: "Card declined");

        var (_, booking) = await api.SendAsync(HttpMethod.Get, $"/api/v1/bookings/{bookingId}", null, traveler);
        booking.GetProperty("status").GetString().Should().Be("Pending");
        var retry = await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payments", null, traveler);
        retry.Body.GetProperty("paymentId").GetString().Should().NotBe(paymentId);
    }

    [Fact]
    public async Task Booking_shows_the_last_payment_attempt_and_why_it_failed()
    {
        var (api, traveler, bookingId) = await BookAsync();
        var (_, fresh) = await api.SendAsync(HttpMethod.Get, $"/api/v1/bookings/{bookingId}", null, traveler);
        fresh.GetProperty("lastPaymentStatus").ValueKind.Should().Be(JsonValueKind.Null);

        var paymentId = (await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payments", null, traveler)).Body.GetProperty("paymentId").GetString()!;
        var (_, pending) = await api.SendAsync(HttpMethod.Get, $"/api/v1/bookings/{bookingId}", null, traveler);
        pending.GetProperty("lastPaymentStatus").GetString().Should().Be("Pending");

        await SendWebhookAsync($"evt_{Guid.NewGuid():N}", "payment.failed", paymentId, reason: "Card declined (sandbox)");
        var (_, declined) = await api.SendAsync(HttpMethod.Get, $"/api/v1/bookings/{bookingId}", null, traveler);
        declined.GetProperty("status").GetString().Should().Be("Pending");
        declined.GetProperty("lastPaymentStatus").GetString().Should().Be("Failed");
        declined.GetProperty("lastPaymentFailureReason").GetString().Should().Be("Card declined (sandbox)");

        // Trying again starts a new attempt, which is now the latest one (also in the list of my bookings).
        var retryId = (await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payments", null, traveler)).Body.GetProperty("paymentId").GetString()!;
        await SendWebhookAsync($"evt_{Guid.NewGuid():N}", "payment.succeeded", retryId);
        var (_, mine) = await api.SendAsync(HttpMethod.Get, "/api/v1/me/bookings", null, traveler);
        var paid = mine.EnumerateArray().Single(b => b.GetProperty("id").GetString() == bookingId);
        paid.GetProperty("status").GetString().Should().Be("Confirmed");
        paid.GetProperty("lastPaymentStatus").GetString().Should().Be("Succeeded");
        paid.GetProperty("lastPaymentFailureReason").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Return_url_on_an_allowed_origin_is_passed_to_the_provider()
    {
        var (api, traveler, bookingId) = await BookAsync();
        var returnUrl = $"{TourDockApiFactory.WebOrigin}/bookings/{bookingId}/checkout?step=return";

        var (status, payment) = await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payments", new { returnUrl }, traveler);

        status.Should().Be(HttpStatusCode.OK);
        var paymentId = Guid.Parse(payment.GetProperty("paymentId").GetString()!);
        factory.PaymentProvider.ReturnUrls[paymentId].Should().Be(new Uri(returnUrl));
    }

    [Theory]
    [InlineData("https://evil.example.net/bookings")]
    [InlineData("https://app.example.com.evil.example.net/")]
    [InlineData("http://app.example.com/bookings")]
    [InlineData("https://app.example.com:8443/bookings")]
    [InlineData("https://user@app.example.com/bookings")]
    [InlineData("//app.example.com/bookings")]
    [InlineData("/bookings")]
    [InlineData("javascript:alert(1)")]
    public async Task Return_url_outside_the_allow_list_is_rejected(string returnUrl)
    {
        var (api, traveler, bookingId) = await BookAsync();
        var calls = factory.PaymentProvider.CreatePaymentCalls;

        var (status, problem) = await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payments", new { returnUrl }, traveler);

        status.Should().Be(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").EnumerateObject().Should().ContainSingle(e => e.Name.Equals("returnUrl", StringComparison.OrdinalIgnoreCase));
        factory.PaymentProvider.CreatePaymentCalls.Should().Be(calls, "nothing is sent to the provider");
    }

    [Fact]
    public async Task Provider_outage_returns_503_and_the_retry_works()
    {
        var (api, traveler, bookingId) = await BookAsync();
        factory.PaymentProvider.FailNextCalls = 1;

        var (status, _) = await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payments", null, traveler);
        var retry = await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payments", null, traveler);

        status.Should().Be(HttpStatusCode.ServiceUnavailable);
        retry.Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cancelling_a_paid_booking_refunds_through_the_outbox()
    {
        var (api, traveler, bookingId) = await BookAsync(guests: 2); // departure 10 days out: 100% refund
        await PayAsync(api, traveler, bookingId);

        var (_, cancelled) = await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/cancel", new { reason = "Sick" }, traveler);
        cancelled.GetProperty("refund").GetProperty("amount").GetDecimal().Should().Be(100m);

        // The refund is recorded with the cancellation, then the outbox worker sends it to the provider.
        var refund = await SingleRefundAsync(bookingId);
        refund.Status.Should().Be(RefundStatus.Pending);
        await ProcessOutboxAsync();

        refund = await SingleRefundAsync(bookingId);
        refund.Status.Should().Be(RefundStatus.Requested);
        factory.PaymentProvider.Refunds[refund.Id].Amount.Amount.Should().Be(100m);

        await SendWebhookAsync($"evt_{Guid.NewGuid():N}", "refund.succeeded", refund.Id.ToString());
        (await SingleRefundAsync(bookingId)).Status.Should().Be(RefundStatus.Succeeded);
    }

    [Fact]
    public async Task Outbox_retries_a_refund_after_a_provider_failure()
    {
        var (api, traveler, bookingId) = await BookAsync();
        await PayAsync(api, traveler, bookingId);

        // Drain messages other tests in this class left behind, so the injected failure hits this test's refund.
        await ProcessOutboxAsync();
        await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/cancel", new { }, traveler);

        factory.PaymentProvider.FailNextCalls = 1;
        await ProcessOutboxAsync();
        var refundId = (await SingleRefundAsync(bookingId)).Id.ToString();
        await using (var db = factory.CreateDbContext())
        {
            var message = (await db.OutboxMessages.Where(m => m.ProcessedAt == null).ToListAsync(Ct)).Single(m => m.Payload.Contains(refundId, StringComparison.Ordinal));
            message.Attempts.Should().Be(1);
            message.ProcessedAt.Should().BeNull();
            message.NextAttemptAt.Should().BeAfter(DateTimeOffset.UtcNow);

            // Pretend the backoff has passed.
            message.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync(Ct);
        }

        await ProcessOutboxAsync();
        (await SingleRefundAsync(bookingId)).Status.Should().Be(RefundStatus.Requested);
    }

    [Fact]
    public async Task A_declined_refund_is_retried_then_waits_for_an_admin_who_can_send_it_again()
    {
        var (api, traveler, bookingId) = await BookAsync();
        await PayAsync(api, traveler, bookingId);
        await ProcessOutboxAsync();
        await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/cancel", new { }, traveler);
        await ProcessOutboxAsync();
        var refundId = (await SingleRefundAsync(bookingId)).Id;

        for (var attempt = 1; attempt <= Refund.MaxFailedAttempts; attempt++)
        {
            await SendWebhookAsync($"evt_{Guid.NewGuid():N}", "refund.failed", refundId.ToString(), "Card closed");
            var refund = await SingleRefundAsync(bookingId);
            refund.FailedAttempts.Should().Be(attempt);
            if (attempt == Refund.MaxFailedAttempts)
            {
                refund.Status.Should().Be(RefundStatus.Failed);
                break;
            }

            // Re-sent later, under a new idempotency key (the provider would replay the decline for the old one).
            refund.Status.Should().Be(RefundStatus.Pending);
            await using (var db = factory.CreateDbContext())
            {
                var retry = (await db.OutboxMessages.Where(m => m.ProcessedAt == null).ToListAsync(Ct))
                    .Single(m => m.Payload.Contains(refundId.ToString(), StringComparison.Ordinal));
                retry.NextAttemptAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(10));
                retry.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1);
                await db.SaveChangesAsync(Ct);
            }

            await ProcessOutboxAsync();
            factory.PaymentProvider.RefundKeys.Should().Contain($"{refundId:N}-{attempt}");
        }

        var client = factory.CreateClient();
        (await (await client.GetAsync("/health/ready", Ct)).Content.ReadAsStringAsync(Ct)).Should().Be("Degraded");
        var admin = await api.AdminTokenAsync();
        var (listed, failed) = await api.SendAsync(HttpMethod.Get, "/api/v1/admin/refunds", null, admin);
        listed.Should().Be(HttpStatusCode.OK);
        failed.EnumerateArray().Select(r => r.GetProperty("refundId").GetGuid()).Should().Contain(refundId);
        (await api.SendAsync(HttpMethod.Get, "/api/v1/admin/refunds", null, traveler)).Status.Should().Be(HttpStatusCode.Forbidden);

        var (retried, _) = await api.SendAsync(HttpMethod.Post, $"/api/v1/admin/refunds/{refundId}/retry", new { note = "Customer updated the card" }, admin);
        retried.Should().Be(HttpStatusCode.NoContent);
        await ProcessOutboxAsync();
        (await SingleRefundAsync(bookingId)).Status.Should().Be(RefundStatus.Requested);
        (await (await client.GetAsync("/health/ready", Ct)).Content.ReadAsStringAsync(Ct)).Should().Be("Healthy");
    }

    [Fact]
    public async Task Payment_arriving_after_the_booking_expired_is_refunded_automatically()
    {
        var (api, traveler, bookingId) = await BookAsync(guests: 2);
        var paymentId = (await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payments", null, traveler)).Body.GetProperty("paymentId").GetString()!;

        // The hold runs out and the job releases the seats before the provider reports success.
        await using (var db = factory.CreateDbContext())
        {
            await db.Bookings.Where(b => b.Id == Guid.Parse(bookingId))
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.HoldExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICommandHandler<ExpireHoldsCommand, int>>().Handle(new ExpireHoldsCommand(), Ct);
        }

        await SendWebhookAsync($"evt_{Guid.NewGuid():N}", "payment.succeeded", paymentId);

        var (_, booking) = await api.SendAsync(HttpMethod.Get, $"/api/v1/bookings/{bookingId}", null, traveler);
        booking.GetProperty("status").GetString().Should().Be(nameof(BookingStatus.Expired));
        booking.GetProperty("paidAmount").GetProperty("amount").GetDecimal().Should().Be(100m);
        booking.GetProperty("refundedAmount").GetProperty("amount").GetDecimal().Should().Be(100m, "not \"not charged\"");
        var refund = await SingleRefundAsync(bookingId);
        refund.Amount.Amount.Should().Be(100m);
    }

    [Fact]
    public async Task Cancelling_while_the_payment_succeeds_never_keeps_the_money_or_confirms_a_cancelled_booking()
    {
        // Whichever wins: payment first (confirmed, then cancelled with a policy refund of 100% this far ahead) or
        // cancellation first (the late payment is refunded automatically). Both must end cancelled and fully refunded.
        var outcomes = new List<string>();
        for (var round = 0; round < 12; round++)
        {
            var (api, traveler, bookingId) = await BookAsync(guests: 2);
            var paymentId = (await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payments", null, traveler))
                .Body.GetProperty("paymentId").GetString()!;

            Task<(HttpStatusCode Status, JsonElement Body)> Cancel() =>
                api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/cancel", new { reason = "Changed plans" }, traveler);
            Task<HttpStatusCode> Pay() => SendWebhookAsync($"evt_{Guid.NewGuid():N}", "payment.succeeded", paymentId);

            // Round 0 and 1 fix the order (one each way); the rest race for real.
            Task<(HttpStatusCode Status, JsonElement Body)> cancel;
            Task<HttpStatusCode> paid;
            switch (round)
            {
                case 0:
                    cancel = Task.FromResult(await Cancel());
                    paid = Task.FromResult(await Pay());
                    break;
                case 1:
                    paid = Task.FromResult(await Pay());
                    cancel = Task.FromResult(await Cancel());
                    break;
                default:
                    (cancel, paid) = (Cancel(), Pay());
                    await Task.WhenAll(cancel, paid);
                    break;
            }

            (await cancel).Status.Should().Be(HttpStatusCode.OK);
            (await paid).Should().Be(HttpStatusCode.OK);

            await using var db = factory.CreateDbContext();
            var booking = await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == Guid.Parse(bookingId), Ct);
            var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == Guid.Parse(paymentId), Ct);
            var refunds = await db.Refunds.AsNoTracking().Where(r => r.BookingId == booking.Id).ToListAsync(Ct);

            booking.Status.Should().Be(BookingStatus.Cancelled, "the customer's cancellation always holds");
            payment.Status.Should().Be(PaymentStatus.Succeeded);
            refunds.Sum(r => r.Amount.Amount).Should().Be(payment.Amount.Amount, "everything paid goes back, exactly once");
            booking.PaidAmount.Amount.Should().Be(payment.Amount.Amount, "the booking shows what was charged");
            booking.RefundedAmount.Amount.Should().Be(payment.Amount.Amount, "and that it all goes back");
            outcomes.Add(booking.ConfirmedAt is null ? "cancelled first" : "paid first");
        }

        outcomes.Should().Contain("cancelled first").And.Contain("paid first");
    }

    private async Task<(TestApi Api, string Traveler, string BookingId)> BookAsync(int guests = 1)
    {
        var api = Api();
        var departure = await api.CreateDepartureAsync(capacity: 8, daysAhead: 10);
        var traveler = await api.RegisterUserAsync();
        var (status, booking) = await api.BookAsync(traveler, departure.DepartureId, guests);
        status.Should().Be(HttpStatusCode.Created);
        return (api, traveler, booking.GetProperty("id").GetString()!);
    }

    private async Task PayAsync(TestApi api, string traveler, string bookingId)
    {
        var paymentId = (await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payments", null, traveler)).Body.GetProperty("paymentId").GetString()!;
        (await SendWebhookAsync($"evt_{Guid.NewGuid():N}", "payment.succeeded", paymentId)).Should().Be(HttpStatusCode.OK);
    }

    private async Task<Refund> SingleRefundAsync(string bookingId)
    {
        await using var db = factory.CreateDbContext();
        return await db.Refunds.AsNoTracking().SingleAsync(r => r.BookingId == Guid.Parse(bookingId), Ct);
    }

    private Task ProcessOutboxAsync() => factory.Services.DrainOutboxAsync(Ct);

    private async Task<HttpStatusCode> SendWebhookAsync(string eventId, string type, string reference, string? reason = null)
    {
        var body = JsonSerializer.Serialize(new { id = eventId, type, data = new { reference, reason } });
        using var client = factory.CreateClient();
        return await PostWebhookAsync(client, body, Signature(body, DateTimeOffset.UtcNow));
    }

    private async Task<HttpStatusCode> PostWebhookAsync(HttpClient client, string body, string? signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/payments")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (signature is not null)
        {
            request.Headers.Add("TourDock-Signature", signature);
        }

        using var response = await client.SendAsync(request, Ct);
        return response.StatusCode;
    }

    private static string Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    private static string Signature(string body, DateTimeOffset at)
    {
        var t = at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var v1 = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(TourDockApiFactory.WebhookSecret), Encoding.UTF8.GetBytes($"{t}.{body}")));
        return $"t={t},v1={v1}";
    }
}
