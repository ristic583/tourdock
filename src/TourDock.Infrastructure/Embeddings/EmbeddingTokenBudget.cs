using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TourDock.Infrastructure.Persistence;

namespace TourDock.Infrastructure.Embeddings;

/// <summary>Tokens sent to the embedding provider per model, counted while a budget is set.</summary>
public sealed class EmbeddingTokenUsage
{
    public string Model { get; set; } = "";

    public long Tokens { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class EmbeddingTokenUsageConfiguration : IEntityTypeConfiguration<EmbeddingTokenUsage>
{
    public void Configure(EntityTypeBuilder<EmbeddingTokenUsage> builder)
    {
        builder.ToTable("embedding_token_usage");
        builder.HasKey(u => u.Model);
        builder.Property(u => u.Model).HasMaxLength(100);
    }
}

/// <summary>The token budget is spent: semantic search stays off until it is raised (VOYAGE_TOKEN_BUDGET).</summary>
internal sealed class EmbeddingBudgetExhaustedException(string model, long budget)
    : Exception($"The token budget for {model} ({budget:N0} tokens) is spent.");

/// <summary>
/// A hard ceiling on what the embedding provider can cost, independent of its own billing (Voyage has no spending cap).
/// Every call is counted with the tokens the provider reports, in the database, so restarts and deploys don't reset it.
/// Once the budget is reached no more calls are made: searches fall back to the normal order and the sync job waits.
/// A call that starts just under the budget may go over it by one batch.
/// </summary>
internal sealed class EmbeddingTokenBudget(IServiceScopeFactory scopes, IOptions<VoyageOptions> options, TimeProvider time)
{
    private volatile bool _spent;

    public async Task EnsureRoomAsync(string model, CancellationToken cancellationToken)
    {
        if (options.Value.TokenBudget is not { } budget)
        {
            return;
        }

        if (!_spent)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TourDockDbContext>();
            var used = await db.Set<EmbeddingTokenUsage>().AsNoTracking()
                .Where(u => u.Model == model)
                .Select(u => (long?)u.Tokens)
                .SingleOrDefaultAsync(cancellationToken) ?? 0;
            _spent = used >= budget; // stays spent until a restart with a higher budget
        }

        if (_spent)
        {
            throw new EmbeddingBudgetExhaustedException(model, budget);
        }
    }

    /// <summary>One statement, so concurrent calls never lose each other's tokens.</summary>
    public async Task RecordAsync(string model, long tokens, CancellationToken cancellationToken)
    {
        if (options.Value.TokenBudget is null || tokens <= 0)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TourDockDbContext>();
        var now = time.GetUtcNow();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO embedding_token_usage (model, tokens, updated_at) VALUES ({model}, {tokens}, {now})
            ON CONFLICT (model) DO UPDATE SET tokens = embedding_token_usage.tokens + EXCLUDED.tokens, updated_at = EXCLUDED.updated_at
            """, cancellationToken);
    }
}
