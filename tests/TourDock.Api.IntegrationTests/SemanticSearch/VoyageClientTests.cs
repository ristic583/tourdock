using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TourDock.Api.IntegrationTests.TravelInfo;
using TourDock.Application.Abstractions;
using TourDock.Infrastructure.Embeddings;

namespace TourDock.Api.IntegrationTests.SemanticSearch;

/// <summary>
/// The real Voyage client and the cached query embeddings (HTTP pipeline, resilience, cache) against a stubbed network.
/// No containers: each test builds its own services, so cache and circuit state never leak between tests.
/// </summary>
public sealed class VoyageClientTests : IDisposable
{
    private readonly StubHttpHandler _http = new();
    private readonly List<(string? Auth, JsonElement Body)> _sent = [];
    private ServiceProvider? _services;

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sends_the_model_input_type_and_key_and_returns_vectors_in_input_order()
    {
        RespondWith(2);

        var provider = Services().GetRequiredService<IEmbeddingProvider>();

        var vectors = await provider.EmbedAsync(["first", "second"], EmbeddingInput.Document, Ct);

        vectors.Select(v => v[0]).Should().Equal(0f, 1f); // the stub answers in reverse order; index decides
        _sent.Should().ContainSingle();
        _sent[0].Auth.Should().Be("Bearer test-key");
        _sent[0].Body.GetProperty("model").GetString().Should().Be("voyage-4");
        _sent[0].Body.GetProperty("input_type").GetString().Should().Be("document");
        _sent[0].Body.GetProperty("output_dimension").GetInt32().Should().Be(VoyageOptions.Dimensions);
    }

    [Fact]
    public async Task Query_vectors_are_cached_by_normalized_text()
    {
        RespondWith(1);
        var queries = Services().GetRequiredService<IQueryEmbeddings>();

        (await queries.TryEmbedAsync("Boat  trip ", Ct)).Should().NotBeNull();
        (await queries.TryEmbedAsync("boat trip", Ct)).Should().NotBeNull();

        _http.Calls.Should().Be(1);
        _sent[0].Body.GetProperty("input_type").GetString().Should().Be("query");
    }

    [Fact]
    public async Task A_failing_provider_gives_no_vector_instead_of_an_error()
    {
        _http.ReturnStatus(HttpStatusCode.ServiceUnavailable);

        (await Services().GetRequiredService<IQueryEmbeddings>().TryEmbedAsync("boat trip", Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Without_a_key_semantic_search_is_off_and_nothing_is_sent()
    {
        var queries = Services(apiKey: null).GetRequiredService<IQueryEmbeddings>();

        (await queries.TryEmbedAsync("boat trip", Ct)).Should().BeNull();
        _http.Calls.Should().Be(0);
    }

    /// <summary>Answers with <paramref name="count"/> vectors, last index first; vector i starts with the value i.</summary>
    private void RespondWith(int count) => _http.Respond = async (request, ct) =>
    {
        var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
        lock (_sent)
        {
            _sent.Add((request.Headers.Authorization?.ToString(), body));
        }

        var data = Enumerable.Range(0, count).Reverse().Select(i => new
        {
            index = i,
            embedding = Enumerable.Repeat(0f, VoyageOptions.Dimensions).Select((_, d) => d == 0 ? (float)i : 0.01f).ToArray(),
        });
        var json = JsonSerializer.Serialize(new { data, usage = new { total_tokens = 3 } });
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    };

    private ServiceProvider Services(string? apiKey = "test-key")
    {
        if (_services is not null)
        {
            return _services;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Voyage:ApiKey"] = apiKey })
            .Build();
        var services = new ServiceCollection().AddLogging();
        services.AddEmbeddings(configuration);
        services.AddHttpClient(EmbeddingHttpClients.Voyage).ConfigurePrimaryHttpMessageHandler(() => _http);
        return _services = services.BuildServiceProvider();
    }

    public void Dispose() => _services?.Dispose();
}
