using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;

namespace TourDock.Api.IntegrationTests;

/// <summary>Place autocomplete and "tours near X": seeded cities, earthdistance and its GiST index.</summary>
public sealed class GeoSearchTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private TestApi Api() => new(factory.CreateClient(), Ct);

    [Theory]
    [InlineData("sib", "Šibenik")]
    [InlineData("ŠIBE", "Šibenik")]
    [InlineData("dako", "Đakovo")]
    [InlineData("  herceg  n", "Herceg Novi")]
    [InlineData("beog", "Beograd")]
    [InlineData("Belgr", "Beograd")]
    [InlineData("Ниш", "Niš")]
    [InlineData("cac", "Čačak")]
    [InlineData("ohr", "Ohrid")]
    public async Task Autocomplete_ignores_case_and_accents(string typed, string expected)
    {
        var (status, body) = await Api().SendAsync(HttpMethod.Get, $"/api/v1/cities?q={Uri.EscapeDataString(typed)}", null);

        status.Should().Be(HttpStatusCode.OK);
        body.EnumerateArray().Select(c => c.GetProperty("name").GetString()).Should().Contain(expected);
    }

    [Fact]
    public async Task Autocomplete_puts_places_with_tours_nearby_first()
    {
        var api = Api();
        await api.CreateDepartureAsync(); // meeting point on the Split waterfront

        var names = (await api.SendAsync(HttpMethod.Get, "/api/v1/cities?q=s&limit=20", null)).Body.EnumerateArray().ToList();

        names[0].GetProperty("name").GetString().Should().Be("Split");
        names[0].GetProperty("toursNearby").GetInt32().Should().BeGreaterThan(0);
        (await api.SendAsync(HttpMethod.Get, "/api/v1/cities?q=", null)).Status.Should().Be(HttpStatusCode.BadRequest);
        (await api.SendAsync(HttpMethod.Get, "/api/v1/cities?q=qqqq", null)).Body.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Searching_near_a_place_uses_the_radius_and_reports_the_distance()
    {
        var api = Api();
        var departure = await api.CreateDepartureAsync(); // 43.508, 16.440 (Split)
        var split = await CityIdAsync(api, "Split");
        var hvar = await CityIdAsync(api, "Hvar"); // about 37 km south, across the water

        var nearSplit = await FindAsync(api, $"cityId={split}&sort=Nearest", departure.TourId);
        nearSplit.Should().NotBeNull();
        nearSplit!.Value.GetProperty("distanceKm").GetDouble().Should().BeLessThan(1);

        (await FindAsync(api, $"cityId={hvar}", departure.TourId)).Should().BeNull("25 km is the default radius");
        var wider = await FindAsync(api, $"cityId={hvar}&radiusKm=50", departure.TourId);
        wider!.Value.GetProperty("distanceKm").GetDouble().Should().BeInRange(30, 45);

        // Typed text that names a known place searches around it too, accents optional.
        (await FindAsync(api, "city=split", departure.TourId)).Should().NotBeNull();
        (await FindAsync(api, "city=sibenik", departure.TourId)).Should().BeNull("Šibenik is about 60 km away");
    }

    [Fact]
    public async Task Typed_text_finds_a_place_by_its_other_name()
    {
        var api = Api();
        var beograd = await CityIdAsync(api, "Beograd");

        var (_, body) = await api.SendAsync(HttpMethod.Get, "/api/v1/cities?q=belgrade", null);

        body.EnumerateArray().First().GetProperty("id").GetString().Should().Be(beograd);
    }

    [Theory]
    [InlineData("cro")]
    [InlineData("Hrvatska")]
    public async Task Places_list_a_matching_country_with_its_places_underneath(string typed)
    {
        var api = Api();
        await api.CreateDepartureAsync(); // Split, Croatia

        var (status, body) = await api.SendAsync(HttpMethod.Get, $"/api/v1/places?q={Uri.EscapeDataString(typed)}", null);

        status.Should().Be(HttpStatusCode.OK);
        var croatia = body.GetProperty("countries").EnumerateArray().Single();
        croatia.GetProperty("code").GetString().Should().Be("HR");
        croatia.GetProperty("name").GetString().Should().Be("Croatia");
        croatia.GetProperty("toursCount").GetInt32().Should().BeGreaterThan(0);
        var places = croatia.GetProperty("places").EnumerateArray().ToList();
        places.Should().HaveCount(6);
        places.Should().OnlyContain(p => p.GetProperty("countryCode").GetString() == "HR");
        places[0].GetProperty("toursNearby").GetInt32().Should().BeGreaterThan(0, "places with the most tours nearby come first");
    }

    [Fact]
    public async Task Places_without_a_country_match_are_just_places()
    {
        var (_, cyrillic) = await Api().SendAsync(HttpMethod.Get, $"/api/v1/places?q={Uri.EscapeDataString("Срб")}", null);
        cyrillic.GetProperty("countries").EnumerateArray().Single().GetProperty("code").GetString().Should().Be("RS");

        var (_, body) = await Api().SendAsync(HttpMethod.Get, "/api/v1/places?q=sib", null);
        body.GetProperty("countries").GetArrayLength().Should().Be(0);
        body.GetProperty("cities").EnumerateArray().Select(c => c.GetProperty("name").GetString()).Should().Contain("Šibenik");
        (await Api().SendAsync(HttpMethod.Get, "/api/v1/places?q=", null)).Status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Searching_a_country_by_code_or_typed_name()
    {
        var api = Api();
        var departure = await api.CreateDepartureAsync(); // Split, Croatia

        (await FindAsync(api, "country=HR", departure.TourId)).Should().NotBeNull();
        (await FindAsync(api, "country=RS", departure.TourId)).Should().BeNull();

        // A whole country name typed without picking a suggestion searches the country, not around a place.
        var typed = await FindAsync(api, "city=croatia", departure.TourId);
        typed.Should().NotBeNull();
        typed!.Value.GetProperty("distanceKm").ValueKind.Should().Be(JsonValueKind.Null);
        (await FindAsync(api, "city=Serbia", departure.TourId)).Should().BeNull();
    }

    [Fact]
    public async Task Radius_validation_and_unknown_places()
    {
        var api = Api();
        (await api.SendAsync(HttpMethod.Get, "/api/v1/tours?radiusKm=500&cityId=" + Guid.NewGuid(), null)).Status.Should().Be(HttpStatusCode.BadRequest);
        var unknown = await api.SendAsync(HttpMethod.Get, "/api/v1/tours?cityId=" + Guid.NewGuid(), null);
        unknown.Status.Should().Be(HttpStatusCode.OK);
        unknown.Body.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task The_radius_filter_can_use_the_gist_index()
    {
        await using var db = factory.CreateDbContext();
        await db.Database.OpenConnectionAsync(Ct);
        await db.Database.ExecuteSqlRawAsync("SET enable_seqscan = off", Ct); // a tiny test table would always be scanned

        // The same SQL EF generates: the function is inlined, so earth_box(...) @> ll_to_earth(...) meets the index.
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "EXPLAIN SELECT id FROM tours WHERE td_within_radius(latitude, longitude, 43.5081, 16.4402, 25000)";
        var plan = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                plan.Add(reader.GetString(0));
            }
        }

        string.Join('\n', plan).Should().Contain("ix_tours_location_earth");
    }

    private static async Task<string> CityIdAsync(TestApi api, string name)
    {
        var (_, body) = await api.SendAsync(HttpMethod.Get, $"/api/v1/cities?q={Uri.EscapeDataString(name)}", null);
        return body.EnumerateArray().First(c => c.GetProperty("name").GetString() == name).GetProperty("id").GetString()!;
    }

    /// <summary>The tour in the search results (first 50), or null.</summary>
    private static async Task<JsonElement?> FindAsync(TestApi api, string query, string tourId)
    {
        var (status, body) = await api.SendAsync(HttpMethod.Get, $"/api/v1/tours?{query}&pageSize=50", null);
        status.Should().Be(HttpStatusCode.OK, body.ToString());
        return body.GetProperty("items").EnumerateArray().Cast<JsonElement?>().FirstOrDefault(t => t!.Value.GetProperty("id").GetString() == tourId);
    }
}
