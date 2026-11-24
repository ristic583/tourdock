using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Slots;

namespace TourDock.Api.IntegrationTests;

public sealed class AgencyManagementTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private static readonly TimeZoneInfo Zagreb = TimeZoneInfo.FindSystemTimeZoneById("Europe/Zagreb");

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private TestApi Api() => new(factory.CreateClient(), Ct);

    [Fact]
    public async Task Manager_sets_up_a_tour_and_a_schedule_generates_local_time_departures()
    {
        var api = Api();
        var (token, agencyId) = await api.CreateAgencyAsync();
        var vehicleId = await api.CreateVehicleAsync(token, agencyId, capacity: 6);
        var tourId = await api.CreateTourAsync(token, agencyId);

        var (published, tour) = await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours/{tourId}/publish", null, token);
        published.Should().Be(HttpStatusCode.OK);
        tour.GetProperty("status").GetString().Should().Be("Published");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (created, rule) = await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours/{tourId}/schedule-rules",
            new { vehicleId, days = "Saturday", startTime = "09:00", validFrom = today, validUntil = (DateOnly?)null }, token);
        created.Should().Be(HttpStatusCode.Created);
        rule.GetProperty("departuresCreated").GetInt32().Should().BeInRange(51, 53); // ~52 Saturdays in 365 days

        var (_, slots) = await api.SendAsync(HttpMethod.Get,
            $"/api/v1/agencies/{agencyId}/slots?from={today:yyyy-MM-dd}&to={today.AddDays(30):yyyy-MM-dd}", null, token);
        slots.GetArrayLength().Should().BeGreaterThan(0);
        foreach (var slot in slots.EnumerateArray())
        {
            var local = TimeZoneInfo.ConvertTime(slot.GetProperty("startsAt").GetDateTimeOffset(), Zagreb);
            local.DayOfWeek.Should().Be(DayOfWeek.Saturday);
            local.TimeOfDay.Should().Be(TimeSpan.FromHours(9));
            slot.GetProperty("capacity").GetInt32().Should().Be(6, "capacity is min(tour 10, vehicle 6)");
        }

        // Running the generation job again must not duplicate departures.
        await using var scope = factory.Services.CreateAsyncScope();
        var generate = scope.ServiceProvider.GetRequiredService<ICommandHandler<GenerateSlotsCommand, GenerateSlotsResult>>();
        (await generate.Handle(new GenerateSlotsCommand(), Ct)).Value.DeparturesCreated.Should().Be(0);
    }

    [Fact]
    public async Task Other_users_cannot_see_or_change_an_agency()
    {
        var api = Api();
        var (token, agencyId) = await api.CreateAgencyAsync();
        var tourId = await api.CreateTourAsync(token, agencyId);
        var outsider = await api.RegisterUserAsync();

        (await api.SendAsync(HttpMethod.Get, $"/api/v1/agencies/{agencyId}/tours", null, outsider)).Status.Should().Be(HttpStatusCode.NotFound);
        (await api.SendAsync(HttpMethod.Get, $"/api/v1/agencies/{agencyId}/tours/{tourId}", null, outsider)).Status.Should().Be(HttpStatusCode.NotFound);
        (await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/vehicles", new { name = "X", type = "Boat", capacity = 4 }, outsider))
            .Status.Should().Be(HttpStatusCode.NotFound);

        // Another agency's manager can't reach this tour through their own agency id either.
        var (otherToken, otherAgencyId) = await api.CreateAgencyAsync();
        (await api.SendAsync(HttpMethod.Get, $"/api/v1/agencies/{otherAgencyId}/tours/{tourId}", null, otherToken))
            .Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("bookings")]
    [InlineData("slots")]
    public async Task Date_ranges_over_120_days_are_a_field_error(string list)
    {
        var api = Api();
        var (token, agencyId) = await api.CreateAgencyAsync();

        var (tooLong, problem) = await api.SendAsync(HttpMethod.Get, $"/api/v1/agencies/{agencyId}/{list}?from=2026-01-01&to=2026-06-30", null, token);
        var (backwards, _) = await api.SendAsync(HttpMethod.Get, $"/api/v1/agencies/{agencyId}/{list}?from=2026-03-01&to=2026-01-01", null, token);

        tooLong.Should().Be(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").TryGetProperty("to", out _).Should().BeTrue();
        backwards.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Pending_agency_can_prepare_drafts_but_not_publish()
    {
        var api = Api();
        var (token, agencyId) = await api.CreateAgencyAsync(approve: false);
        var tourId = await api.CreateTourAsync(token, agencyId);

        var (status, body) = await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours/{tourId}/publish", null, token);

        status.Should().Be(HttpStatusCode.Conflict);
        body.GetProperty("code").GetString().Should().Be("Agency.NotApproved");
    }

    [Fact]
    public async Task The_agency_is_emailed_when_approved_and_when_suspended_with_the_reason()
    {
        var api = Api();
        var (_, agencyId) = await api.CreateAgencyAsync(approve: false);
        var admin = await api.AdminTokenAsync();

        await api.SendAsync(HttpMethod.Post, $"/api/v1/admin/agencies/{agencyId}/approve", null, admin);
        await api.SendAsync(HttpMethod.Post, $"/api/v1/admin/agencies/{agencyId}/suspend", new { reason = "Expired boat licence" }, admin);
        await factory.Services.DrainOutboxAsync(Ct);

        var mails = factory.Emails.To("hello@example.com").Where(m => m.TextBody.Contains($"/agency/{agencyId}", StringComparison.Ordinal)).ToList();
        mails.Should().ContainSingle(m => m.Subject == "Blue Coast Charters is approved on TourDock");
        mails.Should().ContainSingle(m => m.Subject == "Blue Coast Charters is suspended on TourDock")
            .Which.HtmlBody.Should().Contain("Reason: Expired boat licence");
    }

    [Fact]
    public async Task Vehicle_cannot_be_on_two_overlapping_departures()
    {
        var api = Api();
        var (token, agencyId) = await api.CreateAgencyAsync();
        var vehicleId = await api.CreateVehicleAsync(token, agencyId);
        var tourId = await api.CreateTourAsync(token, agencyId);
        var start = DateTime.UtcNow.Date.AddDays(10).AddHours(9);

        var first = await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours/{tourId}/slots", new { vehicleId, localStart = start }, token);
        var overlapping = await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours/{tourId}/slots", new { vehicleId, localStart = start.AddHours(2) }, token);

        first.Status.Should().Be(HttpStatusCode.Created);
        overlapping.Status.Should().Be(HttpStatusCode.Conflict);
        overlapping.Body.GetProperty("code").GetString().Should().Be("Slot.VehicleBusy");

        // Cancelling the first departure frees the vehicle.
        var slotId = first.Body.GetProperty("id").GetString();
        var (cancelled, slot) = await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/slots/{slotId}/cancel", new { reason = "Storm warning" }, token);
        cancelled.Should().Be(HttpStatusCode.OK);
        slot.GetProperty("status").GetString().Should().Be("Cancelled");
        (await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours/{tourId}/slots", new { vehicleId, localStart = start.AddHours(2) }, token))
            .Status.Should().Be(HttpStatusCode.Created);

        // ...but a vehicle with upcoming departures can't be deleted.
        (await api.SendAsync(HttpMethod.Delete, $"/api/v1/agencies/{agencyId}/vehicles/{vehicleId}", null, token))
            .Body.GetProperty("code").GetString().Should().Be("Vehicle.InUse");
    }

    [Fact]
    public async Task Overlapping_seasons_are_rejected()
    {
        var api = Api();
        var (token, agencyId) = await api.CreateAgencyAsync();
        var tourId = await api.CreateTourAsync(token, agencyId);
        var url = $"/api/v1/agencies/{agencyId}/tours/{tourId}/seasons";

        (await api.SendAsync(HttpMethod.Post, url, new { from = "2027-07-01", to = "2027-07-31", pricePerPerson = 69m }, token))
            .Status.Should().Be(HttpStatusCode.OK);
        var (status, body) = await api.SendAsync(HttpMethod.Post, url, new { from = "2027-07-15", to = "2027-08-15", pricePerPerson = 79m }, token);

        status.Should().Be(HttpStatusCode.Conflict);
        body.GetProperty("code").GetString().Should().Be("Tour.OverlappingSeason");
    }

    [Fact]
    public async Task Invalid_tour_input_returns_field_errors()
    {
        var api = Api();
        var (token, agencyId) = await api.CreateAgencyAsync();

        var (status, body) = await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours", TestApi.TourPayload(pricePerPerson: -5m), token);

        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("errors").TryGetProperty("pricePerPerson", out _).Should().BeTrue();
    }
}
