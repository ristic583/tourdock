namespace TourDock.Application.Abstractions;

// Semantic search: texts become vectors whose distance reflects meaning, so "calm boat trip for a family" finds a tour
// described as "a gentle cruise, perfect with kids" without sharing a word with it.

/// <summary>What is being embedded. Retrieval models embed a stored text and a search for it slightly differently.</summary>
public enum EmbeddingInput
{
    /// <summary>A text that is stored and searched (a tour).</summary>
    Document,

    /// <summary>What a traveler typed.</summary>
    Query,
}

/// <summary>Turns texts into embedding vectors (implemented with Voyage AI).</summary>
public interface IEmbeddingProvider
{
    /// <summary>False when no API key is configured; semantic features then switch themselves off.</summary>
    bool IsEnabled { get; }

    /// <summary>The model the vectors come from. Vectors from different models can't be compared.</summary>
    string Model { get; }

    /// <summary>One vector per text, in the same order. Throws when the provider fails.</summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, EmbeddingInput input, CancellationToken cancellationToken);
}

/// <summary>
/// A per-client limit on search texts that need a new vector (cached ones are free). The host decides who the client is
/// (the API: the visitor's IP); outside a request everything is allowed.
/// </summary>
public interface ISemanticSearchLimiter
{
    /// <summary>Takes one permit; false when this client has used its share for now.</summary>
    bool TryTake();
}

/// <summary>Embeds search text for ranking, cached; degrades to "no vector" instead of failing the search.</summary>
public interface IQueryEmbeddings
{
    /// <summary>The model query vectors come from (stored tour vectors must be from the same one).</summary>
    string Model { get; }

    /// <summary>The query's vector, or null when semantic search is unavailable (no key, provider slow or down).</summary>
    Task<float[]?> TryEmbedAsync(string text, CancellationToken cancellationToken);
}
