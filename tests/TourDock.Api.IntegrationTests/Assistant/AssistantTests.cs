using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TourDock.Application.Abstractions;
using TourDock.Application.Assistant;
using TourDock.Infrastructure.Assistant;
using TourDock.Infrastructure.Jobs;

namespace TourDock.Api.IntegrationTests.Assistant;

/// <summary>The assistant endpoint: conversations, limits, failures, retention and privacy (the model is a fake).</summary>
public sealed class AssistantTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private TestApi Api() => new(factory.CreateClient(), Ct);

    [Fact]
    public async Task A_conversation_continues_with_its_id_and_returns_the_cited_tours()
    {
        var api = Api();
        var tour = await api.CreateDepartureAsync();
        factory.Assistant.Cites = [new AgentSource($"https://app.example.com/tours/{tour.TourId}", "Sunset Cruise", "")];

        (await api.SendAsync(HttpMethod.Get, "/api/v1/assistant", null)).Body.GetProperty("enabled").GetBoolean().Should().BeTrue();
        var first = await AskAsync(api, null, "A boat trip near Split?");
        var second = await AskAsync(api, first.GetProperty("conversationId").GetString(), "For four people?");

        first.GetProperty("reply").GetString().Should().Be("Answer 1");
        second.GetProperty("reply").GetString().Should().Be("Answer 2", "the stored history came back to the model");
        second.GetProperty("conversationId").GetString().Should().Be(first.GetProperty("conversationId").GetString());
        second.GetProperty("turnsLeft").GetInt32().Should().Be(1);
        var cited = second.GetProperty("tours")[0];
        cited.GetProperty("id").GetString().Should().Be(tour.TourId);
        cited.GetProperty("title").GetString().Should().Be("Sunset Cruise");
    }

    [Fact]
    public async Task Cited_tours_come_with_price_place_photo_slot_and_their_next_three_departures()
    {
        var api = Api();
        var tour = await api.CreateDepartureAsync(title: "Card Cruise", daysAhead: 10);
        var vehicleId = await api.CreateVehicleAsync(tour.AgencyToken, tour.AgencyId);
        foreach (var day in new[] { 13, 11, 12 })
        {
            (await api.SendAsync(HttpMethod.Post, $"/api/v1/agencies/{tour.AgencyId}/tours/{tour.TourId}/slots",
                new { vehicleId, localStart = DateTime.UtcNow.Date.AddDays(day).AddHours(9) }, tour.AgencyToken)).Status
                .Should().Be(HttpStatusCode.Created);
        }

        var gone = Guid.NewGuid();
        factory.Assistant.Cites =
        [
            new AgentSource($"https://app.example.com/tours/{tour.TourId}", "Card Cruise", ""),
            new AgentSource($"https://app.example.com/tours/{gone}", "Withdrawn Tour", ""),
        ];

        var tours = (await AskAsync(api, null, "A boat trip near Split?")).GetProperty("tours");

        var card = tours[0];
        var details = card.GetProperty("details");
        details.GetProperty("city").GetString().Should().Be("Split");
        details.GetProperty("durationMinutes").GetInt32().Should().Be(180);
        details.GetProperty("pricePerPerson").GetProperty("amount").GetDecimal().Should().Be(50m);
        details.GetProperty("pricePerPerson").GetProperty("currency").GetString().Should().Be("EUR");
        var departures = card.GetProperty("departures").EnumerateArray().ToList();
        departures.Should().HaveCount(3, "the chat shows the next three; the tour page has the rest");
        departures.Select(d => d.GetProperty("localStart").GetDateTime().Date).Should().BeInAscendingOrder()
            .And.StartWith(DateTime.UtcNow.Date.AddDays(10));
        departures[0].GetProperty("remainingSeats").GetInt32().Should().Be(8);
        tours[1].GetProperty("id").GetString().Should().Be(gone.ToString(), "the citation stays even when the tour is gone");
        tours[1].GetProperty("details").ValueKind.Should().Be(JsonValueKind.Null);
        tours[1].GetProperty("departures").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_full_conversation_is_refused_and_an_unknown_id_starts_a_new_one()
    {
        var api = Api();
        var id = (await AskAsync(api, null, "one")).GetProperty("conversationId").GetString();
        await AskAsync(api, id, "two");
        await AskAsync(api, id, "three");

        var (status, body) = await SendAsync(api, id, "four");
        status.Should().Be(HttpStatusCode.TooManyRequests);
        body.GetProperty("code").GetString().Should().Be("Assistant.ConversationFull");

        var fresh = await AskAsync(api, Guid.NewGuid().ToString(), "hello");
        fresh.GetProperty("conversationId").GetString().Should().NotBe(id);
    }

    [Fact]
    public async Task A_failed_turn_answers_503_and_saves_nothing()
    {
        var api = Api();
        var id = (await AskAsync(api, null, "first")).GetProperty("conversationId").GetString();

        factory.Assistant.Fail = true;
        try
        {
            var (status, body) = await SendAsync(api, id, "second");
            status.Should().Be(HttpStatusCode.ServiceUnavailable);
            body.GetProperty("code").GetString().Should().Be("Assistant.Unavailable");
        }
        finally
        {
            factory.Assistant.Fail = false;
        }

        await using var db = factory.CreateDbContext();
        var saved = await db.Set<AssistantConversation>().SingleAsync(c => c.Id == Guid.Parse(id!), Ct);
        saved.Turns.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_or_overlong_message_is_rejected(string message)
    {
        (await SendAsync(Api(), null, message)).Status.Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync(Api(), null, new string('a', 501))).Status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_daily_cap_is_counted_atomically()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var conversations = scope.ServiceProvider.GetRequiredService<IAssistantConversations>();
        await using var db = factory.CreateDbContext();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var used = await db.Set<AssistantDailyUsage>().Where(u => u.Day == today)
            .Select(u => u.Messages).SingleOrDefaultAsync(Ct);

        (await conversations.TryCountMessageAsync(used + 1, Ct)).Should().BeTrue();
        (await conversations.TryCountMessageAsync(used + 1, Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Expired_conversations_are_deleted_and_message_text_never_reaches_the_logs()
    {
        var api = Api();
        var id = Guid.Parse((await AskAsync(api, null, "secret-plan-xyz")).GetProperty("conversationId").GetString()!);
        await using (var db = factory.CreateDbContext())
        {
            await db.Set<AssistantConversation>().Where(c => c.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct);
        }

        (await factory.Services.GetRequiredService<AssistantRetentionJob>().DeleteExpiredAsync(Ct)).Should().BeGreaterThan(0);

        await using var check = factory.CreateDbContext();
        (await check.Set<AssistantConversation>().AnyAsync(c => c.Id == id, Ct)).Should().BeFalse();
        factory.Logs.Entries.Should().NotContain(e => e.Contains("secret-plan-xyz"));
    }

    [Fact]
    public async Task The_agent_tools_search_read_a_tour_and_list_departures()
    {
        var tour = await Api().CreateDepartureAsync(title: "Agent tools tour", description: "Quiet bay, snorkel gear included.");
        await using var scope = factory.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IAgentTools>();

        var search = await tools.RunAsync("search_tours", Json("""{ "place": "Split", "guests": 2 }"""), Ct);
        var details = await tools.RunAsync("get_tour", Json($$"""{ "tour_id": "{{tour.TourId}}" }"""), Ct);
        var departures = await tools.RunAsync("get_departures", Json($$"""{ "tour_id": "{{tour.TourId}}", "guests": 2 }"""), Ct);
        var bad = await tools.RunAsync("get_tour", Json("""{ "tour_id": "not-an-id" }"""), Ct);

        search.Sources.Should().Contain(s => s.Url == $"{TourDockApiFactory.WebOrigin}/tours/{tour.TourId}");
        details.Sources.Single().Text.Should().Contain("snorkel gear included").And.Contain("Cancellation:");
        departures.Sources.Single().Text.Should().Contain("seats free");
        bad.IsError.Should().BeTrue();
        bad.Error.Should().Contain("tour_id");
    }

    [Fact]
    public async Task Search_tells_the_agent_when_matching_by_description_is_unavailable()
    {
        await Api().CreateDepartureAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IAgentTools>();

        factory.Embeddings.Fail = true;
        try
        {
            var result = await tools.RunAsync("search_tours", Json("""{ "description": "quiet sunset sail", "place": "Split" }"""), Ct);

            result.Note.Should().Contain("unavailable");
            result.ToText().Should().StartWith(result.Note);
        }
        finally
        {
            factory.Embeddings.Fail = false;
        }
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static async Task<JsonElement> AskAsync(TestApi api, string? conversationId, string message)
    {
        var (status, body) = await SendAsync(api, conversationId, message);
        status.Should().Be(HttpStatusCode.OK);
        return body;
    }

    private static Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(TestApi api, string? conversationId, string message) =>
        api.SendAsync(HttpMethod.Post, "/api/v1/assistant/messages", new { conversationId, message }, null);
}
