using TourDock.Application.Abstractions;
using TourDock.Infrastructure.Embeddings;

namespace TourDock.Api.IntegrationTests;

/// <summary>
/// Stands in for Voyage: a bag of words hashed into the real vector size and normalized, so texts that share words are
/// close and unrelated texts are far. Deterministic, offline, and enough to test the plumbing and the ordering.
/// </summary>
public sealed class FakeEmbeddingProvider : IEmbeddingProvider
{
    private int _calls;
    private int _texts;

    public bool IsEnabled { get; set; } = true;

    public string Model => "fake-bag-of-words";

    /// <summary>When true, every call fails the way an unreachable provider would.</summary>
    public bool Fail { get; set; }

    public int Calls => Volatile.Read(ref _calls);

    public int TextsEmbedded => Volatile.Read(ref _texts);

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, EmbeddingInput input, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        if (Fail)
        {
            throw new HttpRequestException("Embedding provider unreachable (test).");
        }

        Interlocked.Add(ref _texts, texts.Count);
        return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Vector).ToList());
    }

    public static float[] Vector(string text)
    {
        var vector = new float[VoyageOptions.Dimensions];
        var words = text.ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 2);
        foreach (var word in words)
        {
            vector[Bucket(word)] += 1;
        }

        var length = MathF.Sqrt(vector.Sum(v => v * v));
        return length == 0 ? vector : vector.Select(v => v / length).ToArray();
    }

    private static readonly char[] Separators = [' ', '\n', '\r', '\t', '.', ',', ':', ';', '!', '?', '-', '(', ')'];

    /// <summary>FNV-1a: stable across runs, unlike string.GetHashCode.</summary>
    private static int Bucket(string word)
    {
        var hash = 2166136261u;
        foreach (var c in word)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return (int)(hash % VoyageOptions.Dimensions);
    }
}
