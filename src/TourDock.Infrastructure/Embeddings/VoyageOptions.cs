using System.ComponentModel.DataAnnotations;

namespace TourDock.Infrastructure.Embeddings;

/// <summary>
/// Voyage AI embeddings. Without an API key semantic search stays off and search works as before.
/// A key from the Voyage platform works with api.voyageai.com; a model key from MongoDB Atlas needs https://ai.mongodb.com/v1/.
/// </summary>
public sealed class VoyageOptions
{
    public const string SectionName = "Voyage";

    /// <summary>The size of the <c>tour_embeddings.embedding</c> column; changing it needs a migration.</summary>
    public const int Dimensions = 1024;

    [Required]
    [Url]
    public string BaseUrl { get; init; } = "https://api.voyageai.com/v1/";

    public string? ApiKey { get; init; }

    [Required]
    public string Model { get; init; } = "voyage-4";

    /// <summary>How long the vector of a search text is reused. The same text always gives the same vector.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "30.00:00:00")]
    public TimeSpan QueryCacheDuration { get; init; } = TimeSpan.FromDays(1);

    /// <summary>A search waits at most this long for a query vector, then falls back to the normal order.</summary>
    [Range(typeof(TimeSpan), "00:00:00.200", "00:00:10")]
    public TimeSpan QueryTimeout { get; init; } = TimeSpan.FromSeconds(2.5);

    /// <summary>Tours per request when syncing. Kept small so one request stays under free-tier token limits.</summary>
    [Range(1, 128)]
    public int BatchSize { get; init; } = 32;

    /// <summary>
    /// Most tokens this deployment may send to Voyage per model, ever (<see cref="EmbeddingTokenBudget"/>). Empty: no
    /// limit and nothing is counted. Compose sets 150 million, below the 200 million free tokens of a new account.
    /// </summary>
    [Range(1, long.MaxValue)]
    public long? TokenBudget { get; init; }
}

/// <summary>Name of the HTTP client, so tests can swap its primary handler for a stub.</summary>
public static class EmbeddingHttpClients
{
    public const string Voyage = "voyage";
}
