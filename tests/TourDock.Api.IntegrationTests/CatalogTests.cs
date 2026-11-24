using System.Net;
using System.Text.Json;
using AwesomeAssertions;

namespace TourDock.Api.IntegrationTests;

public sealed class CatalogTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private TestApi Api() => new(factory.CreateClient(), Ct);

    // Each test uses its own made-up city so tests sharing the database don't see each other's tours.
    private static string NewCity() => $"Testville-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task Search_returns_only_published_tours_of_active_agencies_with_departures()
    {
        var api = Api();
        var city = NewCity();
        var (token, agencyId) = await api.CreateAgencyAsync();
        var vehicleId = await api.CreateVehicleAsync(token, agencyId);

        var visible = await CreateTourWithDepartureAsync(api, token, agencyId, vehicleId, city, "Visible", 50m, daysAhead: 5);
        await CreateTourWithDepartureAsync(api, token, agencyId, vehicleId, city, "Draft", 50m, daysAhead: 6, publish: false);
        await CreateTourAsync(api, token, agencyId, city, "No departures", 50m, publish: true);

        var results = await SearchAsync(api, $"city={city}");

        results.GetProperty("totalCount").GetInt32().Should().Be(1);
        var item = results.GetProperty("items")[0];
        item.GetProperty("id").GetString().Should().Be(visible);
        item.GetProperty("agencyName").GetString().Should().Be("Blue Coast Charters");
        item.GetProperty("nextDepartureAt").ValueKind.Should().Be(JsonValueKind.String);

        // Suspending the agency hides its tours (and invalidates the cached search).
        var admin = await api.AdminTokenAsync();
        await api.SendAsync(HttpMethod.Post, $"/api/v1/admin/agencies/{agencyId}/suspend", new { reason = "Policy check" }, admin);
        (await SearchAsync(api, $"city={city}")).GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Search_shows_the_next_bookable_departure_with_live_seat_counts()
    {
        var api = Api();
        var city = NewCity();
        var (token, agencyId) = await api.CreateAgencyAsync();
        var boat = await api.CreateVehicleAsync(token, agencyId, capacity: 8);
        var tourId = await CreateTourAsync(api, token, agencyId, city, "Seats", 50m, publish: true);
        var soon = await CreateSlotAsync(api, token, agencyId, tourId, boat, daysAhead: 5);
        var later = await CreateSlotAsync(api, token, agencyId, tourId, boat, daysAhead: 6);

        var before = (await SearchAsync(api, $"city={city}&guests=2")).GetProperty("items")[0];
        var next = before.GetProperty("nextDeparture");
        next.GetProperty("id").GetString().Should().Be(soon);
        next.GetProperty("remainingSeats").GetInt32().Should().Be(8);
        next.GetProperty("capacity").GetInt32().Should().Be(8);
        next.GetProperty("localStart").GetString().Should().EndWith("T09:00:00");
        before.GetProperty("nextDepartureAt").GetString().Should().Be(next.GetProperty("startsAt").GetString());
        before.GetProperty("timeZoneId").GetString().Should().Be("Europe/Zagreb");

        // A booking changes the seats right away, even though the search itself is still cached.
        var traveler = await api.RegisterUserAsync();
        (await api.BookAsync(traveler, soon, guests: 7)).Status.Should().Be(HttpStatusCode.Created);

        var forTwo = (await SearchAsync(api, $"city={city}&guests=2")).GetProperty("items")[0].GetProperty("nextDeparture");
        forTwo.GetProperty("id").GetString().Should().Be(later, "the first departure has only one seat left");
        var forOne = (await SearchAsync(api, $"city={city}&guests=1")).GetProperty("items")[0].GetProperty("nextDeparture");
        forOne.GetProperty("id").GetString().Should().Be(soon);
        forOne.GetProperty("remainingSeats").GetInt32().Should().Be(1);

        // Sold out since the search was cached: the tour stays on the page without a departure.
        (await api.BookAsync(traveler, later, guests: 8)).Status.Should().Be(HttpStatusCode.Created);
        var soldOut = (await SearchAsync(api, $"city={city}&guests=2")).GetProperty("items")[0];
        soldOut.GetProperty("nextDeparture").ValueKind.Should().Be(JsonValueKind.Null);
        soldOut.GetProperty("nextDepartureAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Date_and_group_size_filter_out_departures_without_room()
    {
        var api = Api();
        var city = NewCity();
        var (token, agencyId) = await api.CreateAgencyAsync();
        var smallBoat = await api.CreateVehicleAsync(token, agencyId, capacity: 4);
        var bigBoat = await api.CreateVehicleAsync(token, agencyId, capacity: 10);
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(8);

        await CreateTourWithDepartureAsync(api, token, agencyId, smallBoat, city, "Small", 40m, daysAhead: 8);
        var big = await CreateTourWithDepartureAsync(api, token, agencyId, bigBoat, city, "Big", 60m, daysAhead: 8);

        var forSix = await SearchAsync(api, $"city={city}&date={date:yyyy-MM-dd}&guests=6");
        forSix.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).Should().Equal(big);

        var otherDay = await SearchAsync(api, $"city={city}&date={date.AddDays(1):yyyy-MM-dd}");
        otherDay.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Price_filter_sorting_and_pagination()
    {
        var api = Api();
        var city = NewCity();
        var (token, agencyId) = await api.CreateAgencyAsync();
        foreach (var (title, price, day) in new[] { ("Cheap", 30m, 3), ("Mid", 60m, 4), ("Pricey", 90m, 5), ("Luxury", 200m, 6) })
        {
            var vehicle = await api.CreateVehicleAsync(token, agencyId);
            await CreateTourWithDepartureAsync(api, token, agencyId, vehicle, city, title, price, day);
        }

        var page1 = await SearchAsync(api, $"city={city}&maxPrice=100&sort=PriceDesc&pageSize=2&page=1");
        var page2 = await SearchAsync(api, $"city={city}&maxPrice=100&sort=PriceDesc&pageSize=2&page=2");

        page1.GetProperty("totalCount").GetInt32().Should().Be(3);
        page1.GetProperty("totalPages").GetInt32().Should().Be(2);
        Titles(page1).Should().Equal("Pricey", "Mid");
        Titles(page2).Should().Equal("Cheap");
    }

    [Fact]
    public async Task Departures_show_seasonal_price_and_what_can_be_booked()
    {
        var api = Api();
        var (token, agencyId) = await api.CreateAgencyAsync();
        var vehicleId = await api.CreateVehicleAsync(token, agencyId, capacity: 8);
        var tourId = await CreateTourWithDepartureAsync(api, token, agencyId, vehicleId, NewCity(), "Seasonal", 50m, daysAhead: 10);
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
        await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours/{tourId}/seasons",
            new { from = date.AddDays(-1), to = date.AddDays(1), pricePerPerson = 75m }, token);

        var (status, departures) = await api.SendAsync(HttpMethod.Get, $"/api/v1/tours/{tourId}/departures?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}&guests=2");

        status.Should().Be(HttpStatusCode.OK);
        var departure = departures[0];
        departure.GetProperty("pricePerPerson").GetProperty("amount").GetDecimal().Should().Be(75m);
        departure.GetProperty("privatePrice").GetProperty("amount").GetDecimal().Should().Be(450m);
        departure.GetProperty("canBookSeats").GetBoolean().Should().BeTrue();
        departure.GetProperty("canBookPrivately").GetBoolean().Should().BeTrue();
        departure.GetProperty("localStart").GetString().Should().EndWith("T09:00:00");
    }

    [Fact]
    public async Task Unpublished_tour_is_not_public()
    {
        var api = Api();
        var (token, agencyId) = await api.CreateAgencyAsync();
        var tourId = await CreateTourAsync(api, token, agencyId, NewCity(), "Hidden", 50m, publish: false);

        (await api.SendAsync(HttpMethod.Get, $"/api/v1/tours/{tourId}")).Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Search_rejects_oversized_pages()
    {
        var (status, body) = await Api().SendAsync(HttpMethod.Get, "/api/v1/tours?pageSize=500");

        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("errors").TryGetProperty("pageSize", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/v1/tours?city=%00")]
    [InlineData("/api/v1/places?q=sp%00lit")]
    [InlineData("/api/v1/cities?q=%01")]
    public async Task Text_with_control_characters_is_a_validation_error_not_a_server_error(string url)
    {
        var (status, _) = await Api().SendAsync(HttpMethod.Get, url);

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_nul_character_in_a_request_body_is_a_validation_error()
    {
        var api = Api();
        var (token, agencyId) = await api.CreateAgencyAsync();
        var (status, body) = await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/vehicles",
            new { name = "Boat\u0000One", type = "Boat", capacity = 4 }, token);

        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("errors").TryGetProperty("name", out _).Should().BeTrue();
    }

    private static IEnumerable<string?> Titles(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString());

    private static async Task<JsonElement> SearchAsync(TestApi api, string query)
    {
        var (status, body) = await api.SendAsync(HttpMethod.Get, $"/api/v1/tours?{query}");
        status.Should().Be(HttpStatusCode.OK);
        return body;
    }

    private static async Task<string> CreateTourAsync(TestApi api, string token, string agencyId, string city, string title, decimal price, bool publish)
    {
        var (status, tour) = await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours", new
        {
            title,
            description = "Test tour.",
            location = new { countryCode = "HR", city, address = "Pier 1", latitude = 43.5, longitude = 16.4, timeZoneId = "Europe/Zagreb" },
            durationMinutes = 180,
            maxGuests = 10,
            bookingMode = "Both",
            pricePerPerson = price,
            privatePrice = 450m,
        }, token);
        status.Should().Be(HttpStatusCode.Created);
        var tourId = tour.GetProperty("id").GetString()!;
        if (publish)
        {
            (await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours/{tourId}/publish", null, token)).Status.Should().Be(HttpStatusCode.OK);
        }

        return tourId;
    }

    private static async Task<string> CreateSlotAsync(TestApi api, string token, string agencyId, string tourId, string vehicleId, int daysAhead)
    {
        var localStart = DateTime.UtcNow.Date.AddDays(daysAhead).AddHours(9);
        var (status, slot) = await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours/{tourId}/slots", new { vehicleId, localStart }, token);
        status.Should().Be(HttpStatusCode.Created);
        return slot.GetProperty("id").GetString()!;
    }

    private static async Task<string> CreateTourWithDepartureAsync(
        TestApi api, string token, string agencyId, string vehicleId, string city, string title, decimal price, int daysAhead, bool publish = true)
    {
        var tourId = await CreateTourAsync(api, token, agencyId, city, title, price, publish);
        var localStart = DateTime.UtcNow.Date.AddDays(daysAhead).AddHours(9);
        (await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours/{tourId}/slots", new { vehicleId, localStart }, token))
            .Status.Should().Be(HttpStatusCode.Created);
        return tourId;
    }
}
