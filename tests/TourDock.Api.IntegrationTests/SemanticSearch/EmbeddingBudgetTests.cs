using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TourDock.Api.IntegrationTests.TravelInfo;
using TourDock.Application.Abstractions;
using TourDock.Infrastructure.Embeddings;
using TourDock.Infrastructure.Persistence;

namespace TourDock.Api.IntegrationTests.SemanticSearch;

/// <summary>
/// The real Voyage client against a stubbed network and the real database: every call is counted with the tokens Voyage
/// reports, and once the budget is spent nothing more is sent.
/// </summary>
public sealed class EmbeddingBudgetTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private const int TokensPerCall = 6;

    private readonly StubHttpHandler _voyage = new();
    private readonly string _model = $"budget-test-{Guid.NewGuid():N}"; // its own counter in the shared database

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Calls_stop_once_the_budget_is_spent_and_searches_fall_back()
    {
        RespondWithOneVector();
        using var host = factory.WithWebHostBuilder(UseRealVoyage(budget: 10));
        var provider = host.Services.GetRequiredService<IEmbeddingProvider>();

        await provider.EmbedAsync(["first"], EmbeddingInput.Document, Ct);
        await provider.EmbedAsync(["second"], EmbeddingInput.Document, Ct); // starts at 6 of 10, so it may go over
        var spent = async () => await provider.EmbedAsync(["third"], EmbeddingInput.Document, Ct);

        await spent.Should().ThrowAsync<EmbeddingBudgetExhaustedException>();
        (await host.Services.GetRequiredService<IQueryEmbeddings>().TryEmbedAsync("boat trip", Ct)).Should().BeNull();
        _voyage.Calls.Should().Be(2);
        (await UsedTokensAsync(host.Services)).Should().Be(2 * TokensPerCall);
    }

    [Fact]
    public async Task The_count_survives_a_restart()
    {
        RespondWithOneVector();
        using (var first = factory.WithWebHostBuilder(UseRealVoyage(budget: 7)))
        {
            await first.Services.GetRequiredService<IEmbeddingProvider>().EmbedAsync(["first"], EmbeddingInput.Document, Ct);
        }

        using var restarted = factory.WithWebHostBuilder(UseRealVoyage(budget: 7));
        var again = async () =>
            await restarted.Services.GetRequiredService<IEmbeddingProvider>().EmbedAsync(["again"], EmbeddingInput.Document, Ct);

        await again.Should().NotThrowAsync("6 of 7 tokens were used, so one more call may start");
        await again.Should().ThrowAsync<EmbeddingBudgetExhaustedException>();
        _voyage.Calls.Should().Be(2);
    }

    private Action<IWebHostBuilder> UseRealVoyage(long budget) => builder => builder
        .UseSetting("Voyage:ApiKey", "test-key")
        .UseSetting("Voyage:Model", _model)
        .UseSetting("Voyage:TokenBudget", budget.ToString(System.Globalization.CultureInfo.InvariantCulture))
        .ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmbeddingProvider>();
            services.AddSingleton<IEmbeddingProvider, VoyageEmbeddingProvider>();
            services.AddHttpClient(EmbeddingHttpClients.Voyage).ConfigurePrimaryHttpMessageHandler(() => _voyage);
        });

    private void RespondWithOneVector() => _voyage.Respond = (_, _) =>
    {
        var json = JsonSerializer.Serialize(new
        {
            data = new[] { new { index = 0, embedding = Enumerable.Repeat(0.01f, VoyageOptions.Dimensions).ToArray() } },
            usage = new { total_tokens = TokensPerCall },
        });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    };

    private async Task<long> UsedTokensAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TourDockDbContext>();
        return await db.Set<EmbeddingTokenUsage>().Where(u => u.Model == _model).Select(u => u.Tokens).SingleAsync(Ct);
    }
}
