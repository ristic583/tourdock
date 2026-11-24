using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using TourDock.Infrastructure.Jobs;

namespace TourDock.Api.IntegrationTests.SemanticSearch;

/// <summary>Free-text search (q): tour embeddings in pgvector, kept in sync by a job, ranking inside the usual filters.</summary>
public sealed class SemanticSearchTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private TestApi Api() => new(factory.CreateClient(), Ct);

    private TourEmbeddingSyncJob Sync => factory.Services.GetRequiredService<TourEmbeddingSyncJob>();

    [Fact]
    public async Task The_sync_job_embeds_new_and_changed_tours_only()
    {
        var api = Api();
        var tour = await api.CreateDepartureAsync(title: "Kayak and snorkel", description: "Paddle to hidden coves.");

        (await Sync.SyncOnceAsync(Ct)).Should().BeGreaterThan(0);
        (await Sync.SyncOnceAsync(Ct)).Should().Be(0, "nothing changed since the last run");

        var (status, _) = await api.SendAsync(HttpMethod.Put, $"/api/v1/agencies/{tour.AgencyId}/tours/{tour.TourId}", new
        {
            title = "Kayak and snorkel",
            description = "Paddle to hidden coves and snorkel over sea grass.",
            location = new
            {
                countryCode = "HR",
                city = "Split",
                address = "Riva 1",
                latitude = 43.508,
                longitude = 16.440,
                timeZoneId = "Europe/Zagreb",
            },
            durationMinutes = 180,
            maxGuests = 10,
        }, tour.AgencyToken);
        status.Should().Be(HttpStatusCode.OK);

        (await Sync.SyncOnceAsync(Ct)).Should().Be(1, "only the edited tour's text changed");
    }

    [Fact]
    public async Task Free_text_orders_the_filtered_tours_by_meaning()
    {
        var api = Api();
        await api.CreateDepartureAsync(title: "Island wine tasting", description: "Vineyards, olive oil and local cheese.");
        var fishing = await api.CreateDepartureAsync(
            title: "Sunrise fishing trip", description: "Catch fish with local fishermen, rods and bait included.");
        await api.CreateDepartureAsync(title: "Old town walking tour", description: "Palace cellars and a guided walk.");
        await Sync.SyncOnceAsync(Ct);

        var body = await SearchAsync(api, "q=fishing%20with%20local%20fishermen&city=split&guests=2");

        body.GetProperty("semantic").GetBoolean().Should().BeTrue();
        body.GetProperty("items")[0].GetProperty("id").GetString().Should().Be(fishing.TourId);
    }

    [Fact]
    public async Task Search_falls_back_to_the_normal_order_when_the_provider_is_down()
    {
        var api = Api();
        await api.CreateDepartureAsync(title: "Dolphin watching", description: "Spot dolphins from a quiet boat.");
        var plain = await SearchAsync(api, "city=split");

        factory.Embeddings.Fail = true;
        try
        {
            var body = await SearchAsync(api, "q=dolphins%20from%20a%20boat&city=split");

            body.GetProperty("semantic").GetBoolean().Should().BeFalse();
            Ids(body).Should().Equal(Ids(plain), "the normal order (soonest departure) is used");
        }
        finally
        {
            factory.Embeddings.Fail = false;
        }
    }

    [Fact]
    public async Task Without_free_text_the_response_has_no_semantic_flag_and_long_text_is_rejected()
    {
        var api = Api();

        (await SearchAsync(api, "city=split")).GetProperty("semantic").ValueKind.Should().Be(JsonValueKind.Null);
        var (status, _) = await api.SendAsync(HttpMethod.Get, $"/api/v1/tours?q={new string('a', 201)}", null);
        status.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<JsonElement> SearchAsync(TestApi api, string query)
    {
        var (status, body) = await api.SendAsync(HttpMethod.Get, $"/api/v1/tours?{query}&pageSize=50", null);
        status.Should().Be(HttpStatusCode.OK);
        return body;
    }

    private static List<string> Ids(JsonElement body) =>
        body.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("id").GetString()!).ToList();
}
