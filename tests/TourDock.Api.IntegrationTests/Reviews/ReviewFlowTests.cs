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
using TourDock.Infrastructure.Outbox;

namespace TourDock.Api.IntegrationTests.Reviews;

public sealed class ReviewFlowTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private TestApi Api() => new(factory.CreateClient(), Ct);

    [Fact]
    public async Task Traveler_reviews_agency_replies_reader_reports_admin_moderates()
    {
        var api = Api();
        var (departure, traveler, bookingId) = await CompletedBookingAsync(api);

        // The finished trip asks the traveler for a review, and the booking says until when.
        await ProcessOutboxAsync();
        factory.Emails.Sent.Should().Contain(m => m.Subject == "How was Sunset Cruise?" && m.TextBody.Contains(bookingId));
        var (_, booking) = await api.SendAsync(HttpMethod.Get, $"/api/v1/bookings/{bookingId}", null, traveler);
        booking.GetProperty("reviewableUntil").ValueKind.Should().Be(JsonValueKind.String);

        var (status, posted) = await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/review", new { rating = 5, comment = "Dolphins at sunset!" }, traveler);
        status.Should().Be(HttpStatusCode.Created);
        var reviewId = posted.GetProperty("id").GetString()!;
        (await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/review", new { rating = 1 }, traveler)).Status
            .Should().Be(HttpStatusCode.Conflict, "one review per booking");

        // The agency hears about it.
        await ProcessOutboxAsync();
        factory.Emails.Sent.Should().Contain(m => m.Subject == "New 5-star review: Sunset Cruise" && m.TextBody.Contains("Dolphins at sunset!"));

        // Public page: summary, shortened name, trip date.
        var reviews = await PublicReviewsAsync(api, departure.TourId);
        reviews.GetProperty("summary").GetProperty("average").GetDouble().Should().Be(5);
        reviews.GetProperty("summary").GetProperty("count").GetInt32().Should().Be(1);
        reviews.GetProperty("summary").GetProperty("distribution").GetProperty("five").GetInt32().Should().Be(1);
        var item = reviews.GetProperty("reviews").GetProperty("items")[0];
        item.GetProperty("authorName").GetString().Should().Be("Test U.");
        item.GetProperty("comment").GetString().Should().Be("Dolphins at sunset!");
        (await TourRatingAsync(api, departure.TourId)).Should().Be(5);

        // The agency replies publicly; a guide-level non-member can't.
        (status, _) = await api.SendAsync(HttpMethod.Put, $"/api/v1/agencies/{departure.AgencyId}/reviews/{reviewId}/reply",
            new { text = "Thank you, come back soon!" }, departure.AgencyToken);
        status.Should().Be(HttpStatusCode.OK);
        (await api.SendAsync(HttpMethod.Put, $"/api/v1/agencies/{departure.AgencyId}/reviews/{reviewId}/reply", new { text = "x" }, traveler)).Status
            .Should().Be(HttpStatusCode.NotFound);
        (await PublicReviewsAsync(api, departure.TourId)).GetProperty("reviews").GetProperty("items")[0]
            .GetProperty("agencyReply").GetString().Should().Be("Thank you, come back soon!");

        // Someone reports it (twice is fine); it doesn't disappear by itself.
        var reader = await api.RegisterUserAsync();
        for (var i = 0; i < 2; i++)
        {
            (await api.SendAsync(HttpMethod.Post, $"/api/v1/reviews/{reviewId}/reports", new { reason = "Spam", note = "Looks fake" }, reader)).Status
                .Should().Be(HttpStatusCode.NoContent);
        }

        (await api.SendAsync(HttpMethod.Post, $"/api/v1/reviews/{reviewId}/reports", new { reason = "Spam" }, traveler)).Status
            .Should().Be(HttpStatusCode.BadRequest, "authors can't report themselves");
        (await PublicReviewsAsync(api, departure.TourId)).GetProperty("summary").GetProperty("count").GetInt32().Should().Be(1);

        // Admin queue → hide → gone from the page and the rating; audited.
        var admin = await api.AdminTokenAsync();
        (await api.SendAsync(HttpMethod.Get, "/api/v1/admin/reviews?reported=true", null, traveler)).Status.Should().Be(HttpStatusCode.Forbidden);
        var (_, queue) = await api.SendAsync(HttpMethod.Get, "/api/v1/admin/reviews?reported=true&pageSize=50", null, admin);
        var queued = queue.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetString() == reviewId);
        queued.GetProperty("openReports").GetInt32().Should().Be(1);
        queued.GetProperty("reportReasons")[0].GetString().Should().Be("Spam");

        (await api.SendAsync(HttpMethod.Post, $"/api/v1/admin/reviews/{reviewId}/hide", new { reason = "Reported as fake; under review" }, admin)).Status
            .Should().Be(HttpStatusCode.NoContent);
        (await PublicReviewsAsync(api, departure.TourId)).GetProperty("summary").GetProperty("count").GetInt32().Should().Be(0);
        (await TourRatingAsync(api, departure.TourId)).Should().BeNull("the catalog cache is cleared when a review is hidden");
        (_, queue) = await api.SendAsync(HttpMethod.Get, "/api/v1/admin/reviews?reported=true&pageSize=50", null, admin);
        queue.GetProperty("items").EnumerateArray().Should().NotContain(r => r.GetProperty("id").GetString() == reviewId, "hiding resolves the reports");

        var (_, log) = await api.SendAsync(HttpMethod.Get, "/api/v1/admin/audit-log?pageSize=50", null, admin);
        log.GetProperty("items").EnumerateArray().Should().Contain(a =>
            a.GetProperty("action").GetString() == "Review.Hide" && a.GetProperty("targetId").GetString() == reviewId
            && a.GetProperty("reason").GetString() == "Reported as fake; under review"
            && a.GetProperty("targetName").GetString()!.EndsWith("review of “Sunset Cruise”", StringComparison.Ordinal)
            && a.GetProperty("tourId").GetString() == departure.TourId);
        log.GetProperty("items").EnumerateArray().Should().Contain(a =>
            a.GetProperty("action").GetString() == "Agency.Approve" && a.GetProperty("targetId").GetString() == departure.AgencyId
            && !string.IsNullOrEmpty(a.GetProperty("targetName").GetString()));

        // The agency still sees it, marked hidden; the author sees it in their list.
        var (_, agencyReviews) = await api.SendAsync(HttpMethod.Get, $"/api/v1/agencies/{departure.AgencyId}/reviews", null, departure.AgencyToken);
        agencyReviews.GetProperty("items")[0].GetProperty("isHidden").GetBoolean().Should().BeTrue();
        var (_, mine) = await api.SendAsync(HttpMethod.Get, "/api/v1/me/reviews", null, traveler);
        mine[0].GetProperty("bookingId").GetString().Should().Be(bookingId);

        // Unhide, then the author deletes it.
        (await api.SendAsync(HttpMethod.Post, $"/api/v1/admin/reviews/{reviewId}/unhide", null, admin)).Status.Should().Be(HttpStatusCode.NoContent);
        (await PublicReviewsAsync(api, departure.TourId)).GetProperty("summary").GetProperty("count").GetInt32().Should().Be(1);
        (await api.SendAsync(HttpMethod.Delete, $"/api/v1/bookings/{bookingId}/review", null, traveler)).Status.Should().Be(HttpStatusCode.NoContent);
        (await PublicReviewsAsync(api, departure.TourId)).GetProperty("summary").GetProperty("count").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Another_agency_cannot_reply_to_or_remove_the_reply_on_a_review()
    {
        var api = Api();
        var (departure, traveler, bookingId) = await CompletedBookingAsync(api);
        var (_, posted) = await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/review", new { rating = 3, comment = "Fine" }, traveler);
        var reviewId = posted.GetProperty("id").GetString()!;
        (await api.SendAsync(HttpMethod.Put, $"/api/v1/agencies/{departure.AgencyId}/reviews/{reviewId}/reply", new { text = "Thank you" }, departure.AgencyToken))
            .Status.Should().Be(HttpStatusCode.OK);
        var (otherManager, otherAgency) = await api.CreateAgencyAsync();

        foreach (var agencyId in new[] { otherAgency, departure.AgencyId })
        {
            (await api.SendAsync(HttpMethod.Put, $"/api/v1/agencies/{agencyId}/reviews/{reviewId}/reply", new { text = "Not ours" }, otherManager))
                .Status.Should().Be(HttpStatusCode.NotFound);
            (await api.SendAsync(HttpMethod.Delete, $"/api/v1/agencies/{agencyId}/reviews/{reviewId}/reply", null, otherManager))
                .Status.Should().Be(HttpStatusCode.NotFound);
        }

        var (_, mine) = await api.SendAsync(HttpMethod.Get, "/api/v1/me/reviews", null, traveler);
        mine.EnumerateArray().Single(r => r.GetProperty("id").GetString() == reviewId).GetProperty("agencyReply").GetString().Should().Be("Thank you");
    }

    [Fact]
    public async Task Only_the_traveler_can_review_and_only_after_the_trip()
    {
        var api = Api();
        var departure = await api.CreateDepartureAsync(daysAhead: 10);
        var traveler = await api.RegisterUserAsync();
        var bookingId = (await api.BookAsync(traveler, departure.DepartureId, 1)).Body.GetProperty("id").GetString()!;

        var (status, problem) = await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/review", new { rating = 5 }, traveler);
        status.Should().Be(HttpStatusCode.Conflict);
        problem.GetProperty("code").GetString().Should().Be("Review.BookingNotCompleted");

        (await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/review", new { rating = 5 }, await api.RegisterUserAsync())).Status
            .Should().Be(HttpStatusCode.NotFound, "someone else's booking doesn't exist for you");
        (await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/review", new { rating = 5 })).Status
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rating_sort_and_unknown_tours()
    {
        var api = Api();
        (await api.SendAsync(HttpMethod.Get, "/api/v1/tours?sort=Rating")).Status.Should().Be(HttpStatusCode.OK, "the weighted rating sort translates to SQL");
        (await api.SendAsync(HttpMethod.Get, $"/api/v1/tours/{Guid.NewGuid()}/reviews")).Status.Should().Be(HttpStatusCode.NotFound);
        (await api.SendAsync(HttpMethod.Get, $"/api/v1/tours/{Guid.NewGuid()}/reviews?pageSize=500")).Status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Replying_to_an_older_review_returns_it_with_the_reply()
    {
        var api = Api();
        var (departure, traveler, bookingId) = await CompletedBookingAsync(api);
        var (_, posted) = await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/review", new { rating = 4, comment = "The first one" }, traveler);
        var reviewId = posted.GetProperty("id").GetString()!;

        // 50 newer reviews push this one off the agency's first page (copies of the same booking and review).
        await using (var db = factory.CreateDbContext())
        {
            var sql = $"""
                DO $$ DECLARE nb uuid; BEGIN FOR i IN 1..50 LOOP nb := gen_random_uuid();
                INSERT INTO bookings SELECT (jsonb_populate_record(NULL::bookings,
                    to_jsonb(b) || jsonb_build_object('id', nb, 'idempotency_key', NULL))).* FROM bookings b WHERE b.id = '{Guid.Parse(bookingId)}';
                INSERT INTO reviews SELECT (jsonb_populate_record(NULL::reviews, to_jsonb(r) || jsonb_build_object(
                    'id', gen_random_uuid(), 'booking_id', nb, 'created_at', now() + i * interval '1 second'))).* FROM reviews r WHERE r.id = '{Guid.Parse(reviewId)}';
                END LOOP; END $$;
                """;
            await db.Database.ExecuteSqlRawAsync(sql, Ct);
        }

        var (status, reply) = await api.SendAsync(HttpMethod.Put, $"/api/v1/agencies/{departure.AgencyId}/reviews/{reviewId}/reply",
            new { text = "Thanks for coming!" }, departure.AgencyToken);

        status.Should().Be(HttpStatusCode.OK);
        reply.GetProperty("id").GetString().Should().Be(reviewId);
        reply.GetProperty("agencyReply").GetString().Should().Be("Thanks for coming!");
    }

    private async Task<(Departure Departure, string Traveler, string BookingId)> CompletedBookingAsync(TestApi api)
    {
        var departure = await api.CreateDepartureAsync(daysAhead: 10);
        var traveler = await api.RegisterUserAsync();
        var bookingId = (await api.BookAsync(traveler, departure.DepartureId, 1)).Body.GetProperty("id").GetString()!;
        var paymentId = (await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payments", null, traveler)).Body.GetProperty("paymentId").GetString()!;
        (await SendWebhookAsync("payment.succeeded", paymentId)).Should().Be(HttpStatusCode.OK);

        // Pretend the trip happened: move the booking's end into the past and run the completion job.
        await using (var db = factory.CreateDbContext())
        {
            await db.Bookings.Where(b => b.Id == Guid.Parse(bookingId))
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.SlotEndsAt, DateTimeOffset.UtcNow.AddHours(-1)), Ct);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICommandHandler<CompleteFinishedBookingsCommand, int>>().Handle(new CompleteFinishedBookingsCommand(), Ct);
        }

        return (departure, traveler, bookingId);
    }

    private static async Task<JsonElement> PublicReviewsAsync(TestApi api, string tourId)
    {
        var (status, body) = await api.SendAsync(HttpMethod.Get, $"/api/v1/tours/{tourId}/reviews");
        status.Should().Be(HttpStatusCode.OK);
        return body;
    }

    private static async Task<double?> TourRatingAsync(TestApi api, string tourId)
    {
        var (_, tour) = await api.SendAsync(HttpMethod.Get, $"/api/v1/tours/{tourId}");
        return tour.GetProperty("averageRating").ValueKind == JsonValueKind.Null ? null : tour.GetProperty("averageRating").GetDouble();
    }

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
