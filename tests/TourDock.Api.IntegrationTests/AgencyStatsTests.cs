using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Payments;

namespace TourDock.Api.IntegrationTests;

public sealed class AgencyStatsTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private TestApi Api() => new(factory.CreateClient(), Ct);

    [Fact]
    public async Task Stats_add_up_departures_seats_revenue_today_and_holds()
    {
        var api = Api();
        var (token, agencyId) = await api.CreateAgencyAsync();
        var boat = await api.CreateVehicleAsync(token, agencyId, capacity: 8);
        var tourId = await api.CreateTourAsync(token, agencyId, pricePerPerson: 50m);
        (await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours/{tourId}/publish", null, token)).Status.Should().Be(HttpStatusCode.OK);
        var inFive = await CreateSlotAsync(api, token, agencyId, tourId, boat, daysAhead: 5);
        var soonToday = await CreateSlotAsync(api, token, agencyId, tourId, boat, daysAhead: 6);
        var outside = await CreateSlotAsync(api, token, agencyId, tourId, boat, daysAhead: 40);

        var traveler = await api.RegisterUserAsync();
        await BookAndPayAsync(api, traveler, inFive, guests: 2);                     // 100 paid
        var cancelled = await BookAndPayAsync(api, traveler, inFive, guests: 1);     // 50 paid, 50 refunded
        (await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{cancelled}/cancel", new { }, traveler)).Status.Should().Be(HttpStatusCode.OK);
        (await api.BookAsync(traveler, inFive, guests: 1)).Status.Should().Be(HttpStatusCode.Created); // on hold
        await BookAndPayAsync(api, traveler, soonToday, guests: 3);                 // 150 paid, departs today
        await BookAndPayAsync(api, traveler, outside, guests: 4);                   // outside the range

        // Move the second departure to later today (local date at the tour) so it counts as "today".
        var startsAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startsAt, TimeZoneInfo.FindSystemTimeZoneById("Europe/Zagreb")).DateTime);
        await using (var db = factory.CreateDbContext())
        {
            var id = Guid.Parse(soonToday);
            await db.Slots.Where(s => s.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.StartsAt, startsAt).SetProperty(x => x.EndsAt, startsAt.AddHours(3)).SetProperty(x => x.LocalDate, localToday), Ct);
            await db.Bookings.Where(b => b.SlotId == id).ExecuteUpdateAsync(s => s.SetProperty(b => b.SlotStartsAt, startsAt), Ct);
        }

        var from = localToday;
        var (status, stats) = await api.SendAsync(HttpMethod.Get, $"/api/v1/agencies/{agencyId}/stats?from={from:yyyy-MM-dd}&to={from.AddDays(30):yyyy-MM-dd}", null, token);

        status.Should().Be(HttpStatusCode.OK);
        stats.GetProperty("departures").GetInt32().Should().Be(2);
        stats.GetProperty("vehiclesInUse").GetInt32().Should().Be(1);
        stats.GetProperty("seatsOnSale").GetInt32().Should().Be(16);
        stats.GetProperty("seatsSold").GetInt32().Should().Be(6, "2 paid + 1 on hold on the first departure, 3 on today's");
        stats.GetProperty("occupancyPercent").GetInt32().Should().Be(38);
        var revenue = stats.GetProperty("revenue");
        revenue.GetProperty("paid").GetProperty("amount").GetDecimal().Should().Be(300m);
        revenue.GetProperty("refunded").GetProperty("amount").GetDecimal().Should().Be(50m);
        revenue.GetProperty("net").GetProperty("amount").GetDecimal().Should().Be(250m);
        revenue.GetProperty("net").GetProperty("currency").GetString().Should().Be("EUR");
        stats.GetProperty("today").GetProperty("departures").GetInt32().Should().Be(1);
        stats.GetProperty("today").GetProperty("guests").GetInt32().Should().Be(3);
        stats.GetProperty("pendingHolds").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Empty_agency_has_zero_stats()
    {
        var api = Api();
        var (token, agencyId) = await api.CreateAgencyAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var (status, stats) = await api.SendAsync(HttpMethod.Get, $"/api/v1/agencies/{agencyId}/stats?from={today:yyyy-MM-dd}&to={today.AddDays(6):yyyy-MM-dd}", null, token);

        status.Should().Be(HttpStatusCode.OK);
        stats.GetProperty("departures").GetInt32().Should().Be(0);
        stats.GetProperty("occupancyPercent").GetInt32().Should().Be(0);
        stats.GetProperty("revenue").GetProperty("net").GetProperty("amount").GetDecimal().Should().Be(0m);
        stats.GetProperty("today").GetProperty("guests").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Only_members_and_admins_see_stats_and_the_range_is_validated()
    {
        var api = Api();
        var (_, agencyId) = await api.CreateAgencyAsync();
        var url = $"/api/v1/agencies/{agencyId}/stats?from=2027-01-01&to=2027-01-31";

        (await api.SendAsync(HttpMethod.Get, url, null, await api.RegisterUserAsync())).Status.Should().Be(HttpStatusCode.NotFound);
        (await api.SendAsync(HttpMethod.Get, url)).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await api.SendAsync(HttpMethod.Get, url, null, await api.AdminTokenAsync())).Status.Should().Be(HttpStatusCode.OK);

        var admin = await api.AdminTokenAsync();
        var (backwards, problem) = await api.SendAsync(HttpMethod.Get, $"/api/v1/agencies/{agencyId}/stats?from=2027-02-01&to=2027-01-01", null, admin);
        backwards.Should().Be(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Object);
        (await api.SendAsync(HttpMethod.Get, $"/api/v1/agencies/{agencyId}/stats?from=2027-01-01&to=2028-01-01", null, admin))
            .Status.Should().Be(HttpStatusCode.OK, "366 days including both ends is the maximum");
        (await api.SendAsync(HttpMethod.Get, $"/api/v1/agencies/{agencyId}/stats?from=2027-01-01&to=2028-01-02", null, admin))
            .Status.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<string> BookAndPayAsync(TestApi api, string traveler, string departureId, int guests)
    {
        var (status, booking) = await api.BookAsync(traveler, departureId, guests);
        status.Should().Be(HttpStatusCode.Created);
        var bookingId = booking.GetProperty("id").GetString()!;
        var paymentId = (await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payments", null, traveler)).Body.GetProperty("paymentId").GetString()!;

        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<HandlePaymentEventCommand, Unit>>();
        (await handler.Handle(new HandlePaymentEventCommand($"evt_{Guid.NewGuid():N}", PaymentEventType.PaymentSucceeded, Guid.Parse(paymentId), null), Ct))
            .IsSuccess.Should().BeTrue();
        return bookingId;
    }

    private static async Task<string> CreateSlotAsync(TestApi api, string token, string agencyId, string tourId, string vehicleId, int daysAhead)
    {
        var localStart = DateTime.UtcNow.Date.AddDays(daysAhead).AddHours(9);
        var (status, slot) = await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours/{tourId}/slots", new { vehicleId, localStart }, token);
        status.Should().Be(HttpStatusCode.Created);
        return slot.GetProperty("id").GetString()!;
    }
}
