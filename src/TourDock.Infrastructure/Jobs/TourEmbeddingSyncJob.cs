using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TourDock.Application.Abstractions;
using TourDock.Application.Catalog;
using TourDock.Domain.Tours;
using TourDock.Infrastructure.Embeddings;
using TourDock.Infrastructure.Persistence;

namespace TourDock.Infrastructure.Jobs;

/// <summary>
/// Keeps tour embeddings in step with the tours. Every run compares a hash of each published tour's text (and the model)
/// with the stored one and embeds only what changed, in small batches. Reconciling instead of reacting to each edit means
/// tour commands don't know about embeddings, a provider outage or a model change heals on the next run, and the demo
/// seed and old tours need no separate backfill.
/// </summary>
public sealed partial class TourEmbeddingSyncJob(
    IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider time, IOptions<VoyageOptions> options,
    ILogger<TourEmbeddingSyncJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Jobs:Embeddings:Enabled", true))
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(configuration.GetValue("Jobs:Embeddings:IntervalSeconds", 60)));
        do
        {
            try
            {
                await SyncOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex.GetType().Name, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One reconciliation pass; returns how many tours were embedded. Public so tests can run it directly.</summary>
    public async Task<int> SyncOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<IEmbeddingProvider>();
        if (!provider.IsEnabled)
        {
            return 0;
        }

        var db = scope.ServiceProvider.GetRequiredService<TourDockDbContext>();
        var store = new TourEmbeddingStore(db);
        var stale = await FindStaleAsync(db, store, provider.Model, cancellationToken);

        var embedded = 0;
        foreach (var batch in stale.Chunk(options.Value.BatchSize))
        {
            var vectors = await provider.EmbedAsync(batch.Select(t => t.Document).ToList(), EmbeddingInput.Document, cancellationToken);
            for (var i = 0; i < batch.Length; i++)
            {
                await store.UpsertAsync(batch[i].TourId, provider.Model, batch[i].Hash, vectors[i], time.GetUtcNow(), cancellationToken);
            }

            embedded += batch.Length;
        }

        if (embedded > 0)
        {
            LogEmbedded(logger, embedded, provider.Model);
        }

        return embedded;
    }

    private sealed record StaleTour(Guid TourId, string Document, string Hash);

    private static async Task<List<StaleTour>> FindStaleAsync(
        TourDockDbContext db, TourEmbeddingStore store, string model, CancellationToken cancellationToken)
    {
        var tours = await db.Tours.AsNoTracking()
            .Where(t => t.Status == TourStatus.Published)
            .OrderBy(t => t.Id)
            .Select(t => new
            {
                t.Id,
                Input = new TourDocumentInput(
                    t.Title, t.Description, t.Location.City, t.Location.CountryCode, t.Duration, t.BookingMode),
            })
            .ToListAsync(cancellationToken);
        var stored = await store.GetHashesAsync(model, cancellationToken);

        return tours
            .Select(t => (t.Id, Document: TourDocument.Build(t.Input)))
            .Select(t => new StaleTour(t.Id, t.Document, TourDocument.Hash(model, t.Document)))
            .Where(t => !stored.TryGetValue(t.TourId, out var hash) || hash != t.Hash)
            .ToList();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Embedding sync: {Count} tours embedded with {Model}")]
    private static partial void LogEmbedded(ILogger logger, int count, string model);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Embedding sync failed ({Reason}); will retry on the next run")]
    private static partial void LogFailed(ILogger logger, string reason, Exception exception);
}
