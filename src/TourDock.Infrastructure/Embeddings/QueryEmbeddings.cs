using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TourDock.Application.Abstractions;

namespace TourDock.Infrastructure.Embeddings;

/// <summary>
/// Search-text vectors, cached by the normalized text (the same text always gives the same vector, so popular searches
/// cost one provider call a day). A slow or failing provider never fails a search: it returns null within
/// <see cref="VoyageOptions.QueryTimeout"/> and search falls back to its normal order.
/// </summary>
internal sealed partial class QueryEmbeddings(
    IEmbeddingProvider provider, HybridCache cache, ISemanticSearchLimiter limiter, IOptions<VoyageOptions> options,
    ILogger<QueryEmbeddings> logger) : IQueryEmbeddings
{
    public string Model => provider.Model;

    public async Task<float[]?> TryEmbedAsync(string text, CancellationToken cancellationToken)
    {
        var normalized = Normalize(text);
        if (!provider.IsEnabled || normalized.Length == 0)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.QueryTimeout);
        try
        {
            // The key holds a hash, not the text: bounded length, and what visitors type doesn't sit in cache keys.
            var key = $"embedding:query:{provider.Model}:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))}";
            if (await cache.GetOrCreateAsync<float[]?>(key, static _ => ValueTask.FromResult<float[]?>(null), CachedOnly,
                    cancellationToken: timeout.Token) is { } cached)
            {
                return cached;
            }

            // Only a text that needs a new vector takes one of the client's permits. Asked here, inside the request: the
            // cache may run its factory on another thread, where the request isn't visible.
            if (!limiter.TryTake())
            {
                LogClientLimited();
                return null;
            }

            var duration = options.Value.QueryCacheDuration;
            return await cache.GetOrCreateAsync(
                key,
                (provider, normalized),
                static async (state, ct) => (await state.provider.EmbedAsync([state.normalized], EmbeddingInput.Query, ct))[0],
                new HybridCacheEntryOptions { Expiration = duration, LocalCacheExpiration = duration },
                cancellationToken: timeout.Token);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogUnavailable(ex.GetType().Name); // never the text itself (privacy rule S8)
            return null;
        }
    }

    /// <summary>Trimmed, single spaces, lower case: "Boat  Trip " and "boat trip" share one vector.</summary>
    internal static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Semantic search unavailable ({Reason}); search falls back to the normal order")]
    private partial void LogUnavailable(string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Semantic search limit reached for a client; normal order instead")]
    private partial void LogClientLimited();

    /// <summary>A lookup that never calls the provider and stores nothing.</summary>
    private static readonly HybridCacheEntryOptions CachedOnly = new()
    {
        Flags = HybridCacheEntryFlags.DisableUnderlyingData | HybridCacheEntryFlags.DisableLocalCacheWrite
            | HybridCacheEntryFlags.DisableDistributedCacheWrite,
    };
}

/// <summary>Outside the API (jobs, tools, tests without a host) nobody is limited.</summary>
internal sealed class UnlimitedSemanticSearch : ISemanticSearchLimiter
{
    public bool TryTake() => true;
}
