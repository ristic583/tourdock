using System.Net;
using AwesomeAssertions;
using AwesomeAssertions.Execution;

namespace TourDock.Api.IntegrationTests;

/// <summary>
/// Tenant isolation for every agency route: another agency's manager and a customer get 404 (not 403, so they don't
/// learn the agency exists), whether they use the agency's id or their own agency's id with its objects.
/// </summary>
public sealed class AuthorizationMatrixTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_agency_route_hides_another_agencys_data()
    {
        var api = new TestApi(factory.CreateClient(), Ct);
        var owner = await api.CreateDepartureAsync();
        var (a, tour, slot) = (owner.AgencyId, owner.TourId, owner.DepartureId);
        var (_, vehicles) = await api.SendAsync(HttpMethod.Get, $"/api/v1/agencies/{a}/vehicles", null, owner.AgencyToken);
        var vehicle = vehicles[0].GetProperty("id").GetString()!;
        var customer = await api.RegisterUserAsync();
        var booking = (await api.BookAsync(customer, slot, 1)).Body.GetProperty("id").GetString()!;
        var (otherManager, b) = await api.CreateAgencyAsync();
        var otherTour = await api.CreateTourAsync(otherManager, b);
        var loc = new { countryCode = "HR", city = "Split", address = "Riva 1", latitude = 43.5, longitude = 16.4, timeZoneId = "Europe/Zagreb" };
        var range = "?from=2026-01-01&to=2026-03-01";

        (HttpMethod Method, string Url, object? Body)[] foreignAgencyRoutes =
        [
            (HttpMethod.Get, $"/api/v1/agencies/{a}/vehicles", null),
            (HttpMethod.Post, $"/api/v1/agencies/{a}/vehicles", new { name = "X", type = "Boat", capacity = 4 }),
            (HttpMethod.Put, $"/api/v1/agencies/{a}/vehicles/{vehicle}", new { name = "X", type = "Boat", capacity = 4 }),
            (HttpMethod.Delete, $"/api/v1/agencies/{a}/vehicles/{vehicle}", null),
            (HttpMethod.Get, $"/api/v1/agencies/{a}/tours", null),
            (HttpMethod.Get, $"/api/v1/agencies/{a}/tours/{tour}", null),
            (HttpMethod.Post, $"/api/v1/agencies/{a}/tours", TestApi.TourPayload()),
            (HttpMethod.Put, $"/api/v1/agencies/{a}/tours/{tour}", new { title = "Pwned", description = "Changed by someone else.", location = loc, durationMinutes = 60, maxGuests = 4 }),
            (HttpMethod.Put, $"/api/v1/agencies/{a}/tours/{tour}/pricing", new { bookingMode = "Shared", pricePerPerson = 1m }),
            (HttpMethod.Put, $"/api/v1/agencies/{a}/tours/{tour}/cancellation-policy", new[] { new { hoursBeforeStart = 1, refundPercent = 0 } }),
            (HttpMethod.Post, $"/api/v1/agencies/{a}/tours/{tour}/unpublish", null),
            (HttpMethod.Delete, $"/api/v1/agencies/{a}/tours/{tour}", null),
            (HttpMethod.Post, $"/api/v1/agencies/{a}/tours/{tour}/seasons", new { from = "2027-01-01", to = "2027-01-10", pricePerPerson = 1m }),
            (HttpMethod.Post, $"/api/v1/agencies/{a}/tours/{tour}/slots", new { vehicleId = vehicle, localStart = "2027-02-01T10:00:00" }),
            (HttpMethod.Put, $"/api/v1/agencies/{a}/tours/{tour}/photos/order", new { photoIds = Array.Empty<Guid>() }),
            (HttpMethod.Get, $"/api/v1/agencies/{a}/slots{range}", null),
            (HttpMethod.Post, $"/api/v1/agencies/{a}/slots/{slot}/block", null),
            (HttpMethod.Post, $"/api/v1/agencies/{a}/slots/{slot}/cancel", new { reason = "x" }),
            (HttpMethod.Get, $"/api/v1/agencies/{a}/stats{range}", null),
            (HttpMethod.Get, $"/api/v1/agencies/{a}/bookings{range}", null),
            (HttpMethod.Post, $"/api/v1/agencies/{a}/bookings/{booking}/cancel", new { reason = "x" }),
            (HttpMethod.Get, $"/api/v1/agencies/{a}/reviews", null),
        ];

        // Own agency id, foreign objects.
        (HttpMethod Method, string Url, object? Body)[] ownAgencyForeignObjects =
        [
            (HttpMethod.Get, $"/api/v1/agencies/{b}/tours/{tour}", null),
            (HttpMethod.Put, $"/api/v1/agencies/{b}/vehicles/{vehicle}", new { name = "X", type = "Boat", capacity = 4 }),
            (HttpMethod.Post, $"/api/v1/agencies/{b}/slots/{slot}/block", null),
            (HttpMethod.Post, $"/api/v1/agencies/{b}/bookings/{booking}/cancel", new { reason = "x" }),
            (HttpMethod.Post, $"/api/v1/agencies/{b}/tours/{otherTour}/slots", new { vehicleId = vehicle, localStart = "2027-02-01T10:00:00" }),
        ];

        using var scope = new AssertionScope();
        foreach (var caller in new[] { otherManager, customer })
        {
            foreach (var (method, url, body) in foreignAgencyRoutes)
            {
                (await api.SendAsync(method, url, body, caller)).Status.Should().Be(HttpStatusCode.NotFound, $"{method} {url}");
            }
        }

        foreach (var (method, url, body) in ownAgencyForeignObjects)
        {
            (await api.SendAsync(method, url, body, otherManager)).Status.Should().Be(HttpStatusCode.NotFound, $"{method} {url}");
        }

        // Nothing changed for the owner.
        var (status, ownTour) = await api.SendAsync(HttpMethod.Get, $"/api/v1/agencies/{a}/tours/{tour}", null, owner.AgencyToken);
        status.Should().Be(HttpStatusCode.OK);
        ownTour.GetProperty("title").GetString().Should().Be("Sunset Cruise");
    }

    [Fact]
    public async Task An_outsider_gets_404_even_for_a_malformed_request()
    {
        var api = new TestApi(factory.CreateClient(), Ct);
        var (_, agencyId) = await api.CreateAgencyAsync();
        var (outsider, _) = await api.CreateAgencyAsync();

        // Access is checked before validation: field errors would confirm that the agency exists.
        var (create, _) = await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours", new { title = "x" }, outsider);
        var (stats, _) = await api.SendAsync(HttpMethod.Get, $"/api/v1/agencies/{agencyId}/stats?from=2026-03-01&to=2026-01-01", null, outsider);

        create.Should().Be(HttpStatusCode.NotFound);
        stats.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_suspended_agency_can_read_but_not_change_anything()
    {
        var api = new TestApi(factory.CreateClient(), Ct);
        var owner = await api.CreateDepartureAsync();
        var admin = await api.AdminTokenAsync();
        (await api.SendAsync(HttpMethod.Post, $"/api/v1/admin/agencies/{owner.AgencyId}/suspend", new { reason = "Complaints" }, admin))
            .Status.Should().Be(HttpStatusCode.OK);

        var (read, _) = await api.SendAsync(HttpMethod.Get, $"/api/v1/agencies/{owner.AgencyId}/tours", null, owner.AgencyToken);
        var (write, problem) = await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{owner.AgencyId}/tours/{owner.TourId}/unpublish", null, owner.AgencyToken);
        var (booked, _) = await api.BookAsync(await api.RegisterUserAsync(), owner.DepartureId, 1);

        read.Should().Be(HttpStatusCode.OK);
        write.Should().Be(HttpStatusCode.Forbidden);
        problem.GetProperty("code").GetString().Should().Be("Agency.NotActive");
        booked.Should().Be(HttpStatusCode.Conflict, "a suspended agency's departures can't be booked");
    }
}
