using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TourDock.Infrastructure.Persistence;

namespace TourDock.Infrastructure.Outbox;

/// <summary>How the outbox is doing: what's waiting, how old the oldest waiting message is, and what gave up.</summary>
internal sealed record OutboxStatus(int Pending, TimeSpan? OldestPendingAge, int ParkedLastWeek);

internal static class OutboxStatusQuery
{
    public static async Task<OutboxStatus> ReadAsync(TourDockDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pending = db.OutboxMessages.AsNoTracking().Where(m => m.ProcessedAt == null);
        var count = await pending.CountAsync(cancellationToken);
        var oldest = count == 0 ? (DateTimeOffset?)null : await pending.MinAsync(m => (DateTimeOffset?)m.OccurredAt, cancellationToken);
        var weekAgo = now.AddDays(-7);
        var parked = await db.OutboxMessages.AsNoTracking()
            .CountAsync(m => m.ProcessedAt != null && m.ProcessedAt >= weekAgo && m.Attempts >= OutboxProcessor.MaxAttempts, cancellationToken);
        return new OutboxStatus(count, oldest is null ? null : now - oldest.Value, parked);
    }
}

/// <summary>
/// Degraded (still HTTP 200, so the instance stays in rotation) when messages are piling up or have given up:
/// emails and refunds are late, which someone should look at, but the API itself works.
/// </summary>
internal sealed class OutboxHealthCheck(TourDockDbContext db, TimeProvider time) : IHealthCheck
{
    public static readonly TimeSpan MaxPendingAge = TimeSpan.FromMinutes(10);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = await OutboxStatusQuery.ReadAsync(db, time.GetUtcNow(), cancellationToken);
        var data = new Dictionary<string, object>
        {
            ["pending"] = status.Pending,
            ["oldestPendingSeconds"] = (int)(status.OldestPendingAge?.TotalSeconds ?? 0),
            ["parkedLastWeek"] = status.ParkedLastWeek,
        };

        if (status.ParkedLastWeek > 0)
        {
            return HealthCheckResult.Degraded($"{status.ParkedLastWeek} outbox message(s) gave up in the last 7 days.", data: data);
        }

        return status.OldestPendingAge > MaxPendingAge
            ? HealthCheckResult.Degraded($"The oldest outbox message has waited {status.OldestPendingAge.Value.TotalMinutes:0} minutes.", data: data)
            : HealthCheckResult.Healthy(data: data);
    }
}
