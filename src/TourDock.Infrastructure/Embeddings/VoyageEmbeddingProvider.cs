using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TourDock.Application.Abstractions;

namespace TourDock.Infrastructure.Embeddings;

/// <summary>
/// Voyage AI embeddings over HTTP (there is no official .NET client). Timeouts, retries and the circuit breaker come
/// from the HTTP client's resilience handler (see <see cref="EmbeddingsRegistration"/>).
/// </summary>
internal sealed class VoyageEmbeddingProvider(
    IHttpClientFactory httpClients, IOptions<VoyageOptions> options, EmbeddingTokenBudget budget) : IEmbeddingProvider
{
    private readonly VoyageOptions _options = options.Value;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public string Model => _options.Model;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, EmbeddingInput input, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("Voyage is not configured (Voyage:ApiKey is empty).");
        }

        if (texts.Count == 0)
        {
            return [];
        }

        await budget.EnsureRoomAsync(_options.Model, cancellationToken);
        var http = httpClients.CreateClient(EmbeddingHttpClients.Voyage);
        using var request = new HttpRequestMessage(HttpMethod.Post, "embeddings")
        {
            Content = JsonContent.Create(new EmbeddingRequest(
                texts, _options.Model, input == EmbeddingInput.Query ? "query" : "document", VoyageOptions.Dimensions)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken)
            ?? throw new InvalidDataException("Empty embedding response.");

        // Billed tokens as Voyage reports them; if it ever doesn't, a rough estimate (about 4 characters a token).
        var tokens = body.Usage?.TotalTokens ?? texts.Sum(t => (t.Length / 4) + 1);
        await budget.RecordAsync(_options.Model, tokens, CancellationToken.None); // the call is paid for even if cancelled now

        var vectors = body.Data.OrderBy(d => d.Index).Select(d => d.Embedding).ToList();
        if (vectors.Count != texts.Count || vectors.Any(v => v.Length != VoyageOptions.Dimensions))
        {
            throw new InvalidDataException(
                $"Expected {texts.Count} vectors of {VoyageOptions.Dimensions} dimensions, got {vectors.Count}.");
        }

        return vectors;
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input_type")] string InputType,
        [property: JsonPropertyName("output_dimension")] int OutputDimension);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("data")] List<EmbeddingItem> Data,
        [property: JsonPropertyName("usage")] EmbeddingUsage? Usage);

    private sealed record EmbeddingUsage([property: JsonPropertyName("total_tokens")] long TotalTokens);

    private sealed record EmbeddingItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
