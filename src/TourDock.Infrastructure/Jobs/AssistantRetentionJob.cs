using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TourDock.Infrastructure.Persistence;

namespace TourDock.Infrastructure.Jobs;

/// <summary>Deletes assistant conversations past their expiry, and daily usage counters older than a week.</summary>
public sealed partial class AssistantRetentionJob(
    IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider time, ILogger<AssistantRetentionJob> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Jobs:AssistantRetention:Enabled", true))
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(configuration.GetValue("Jobs:AssistantRetention:IntervalMinutes", 30)));
        do
        {
            try
            {
                await DeleteExpiredAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One pass; returns how many conversations were deleted. Public so tests can run it directly.</summary>
    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TourDockDbContext>();
        var now = time.GetUtcNow();
        var weekAgo = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-7);

        var deleted = await db.AssistantConversations.Where(c => c.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);
        await db.AssistantDailyUsage.Where(u => u.Day < weekAgo).ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            LogDeleted(logger, deleted);
        }

        return deleted;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Assistant retention: {Count} expired conversations deleted")]
    private static partial void LogDeleted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Assistant retention failed; will retry on the next run")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
