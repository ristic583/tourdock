using Microsoft.EntityFrameworkCore;
using TourDock.Infrastructure.Persistence;

namespace TourDock.Infrastructure.Embeddings;

/// <summary>
/// Reads and writes <c>tour_embeddings</c> with parameterized SQL. The table has no EF model, so no .NET type needs to
/// understand pgvector's <c>vector</c>: a <c>real[]</c> parameter is cast to it in SQL.
/// </summary>
internal sealed class TourEmbeddingStore(TourDockDbContext db)
{
    /// <summary>Content hash of every tour embedded with <paramref name="model"/>.</summary>
    public async Task<Dictionary<Guid, string>> GetHashesAsync(string model, CancellationToken cancellationToken)
    {
        var rows = await db.Database
            .SqlQuery<StoredHash>($"SELECT tour_id, content_hash FROM tour_embeddings WHERE model = {model}")
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.TourId, r => r.ContentHash);
    }

    public async Task UpsertAsync(
        Guid tourId, string model, string contentHash, float[] embedding, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO tour_embeddings (tour_id, model, content_hash, embedding, updated_at)
             VALUES ({tourId}, {model}, {contentHash}, {embedding}::vector, {now})
             ON CONFLICT (tour_id) DO UPDATE SET
                 model = EXCLUDED.model,
                 content_hash = EXCLUDED.content_hash,
                 embedding = EXCLUDED.embedding,
                 updated_at = EXCLUDED.updated_at
             """,
            cancellationToken);

    internal sealed record StoredHash(Guid TourId, string ContentHash);
}
