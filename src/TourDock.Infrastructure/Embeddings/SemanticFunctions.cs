using Microsoft.EntityFrameworkCore;

namespace TourDock.Infrastructure.Embeddings;

/// <summary>
/// Semantic distance backed by pgvector. It exists only to be translated to SQL inside LINQ queries (see the migration
/// that creates td_semantic_distance); calling it in .NET throws.
/// </summary>
public static class SemanticFunctions
{
    /// <summary>
    /// Cosine distance (0 = same meaning, 2 = opposite) between a tour's stored embedding and a query embedding made by
    /// the same model. Null when the tour has no embedding from that model yet, so such tours sort after the rest.
    /// </summary>
    public static double? Distance(Guid tourId, string model, float[] query) =>
        throw new InvalidOperationException("Only for use in database queries.");

    internal static void Map(ModelBuilder modelBuilder) =>
        modelBuilder.HasDbFunction(typeof(SemanticFunctions).GetMethod(nameof(Distance))!).HasName("td_semantic_distance");
}
