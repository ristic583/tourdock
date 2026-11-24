using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TourDock.Application.Abstractions.Data;
using TourDock.Infrastructure.Persistence;

namespace TourDock.Infrastructure.Outbox;

/// <summary>
/// Delivers outbox messages at least once. Messages are claimed with FOR UPDATE SKIP LOCKED, so several API instances can
/// run the worker without handling the same message twice concurrently. Failures retry with exponential backoff;
/// after <see cref="MaxAttempts"/> a message is parked (kept with its last error) for manual follow-up.
/// Handlers must be idempotent (they are: status checks + provider idempotency keys).
/// </summary>
public sealed partial class OutboxProcessor(
    IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider time, Application.Observability.TourDockMetrics metrics, ILogger<OutboxProcessor> logger)
    : BackgroundService
{
    public const int MaxAttempts = 10;
    private const int BatchSize = 20;
    private static readonly TimeSpan StatsInterval = TimeSpan.FromSeconds(30);
    private DateTimeOffset _lastStats = DateTimeOffset.MinValue;

    private static readonly Dictionary<string, Type> MessageTypes = typeof(IOutboxMessage).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IOutboxMessage).IsAssignableFrom(t))
        .ToDictionary(t => t.FullName!);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Jobs:Outbox:Enabled", true))
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(configuration.GetValue("Jobs:Outbox:IntervalSeconds", 2)));
        do
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogBatchFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Processes one batch. Public so tests (and admin tools) can trigger it directly.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TourDockDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var now = time.GetUtcNow();
        var messages = await db.OutboxMessages
            .FromSql($"""
                SELECT * FROM outbox_messages
                WHERE processed_at IS NULL AND next_attempt_at <= {now}
                ORDER BY occurred_at
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                await DispatchAsync(message, cancellationToken);
                message.ProcessedAt = time.GetUtcNow();
                message.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                if (message.Attempts >= MaxAttempts)
                {
                    message.ProcessedAt = time.GetUtcNow(); // parked: stop retrying, keep the error for follow-up
                    LogParked(logger, message.Id, message.Type, ex);
                }
                else
                {
                    var delay = TimeSpan.FromSeconds(Math.Min(3600, Math.Pow(2, message.Attempts)));
                    message.NextAttemptAt = time.GetUtcNow() + delay;
                    LogRetry(logger, message.Id, message.Type, message.Attempts, delay.TotalSeconds);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Gauges for the backlog, refreshed now and then rather than on every 2-second tick.
        if (time.GetUtcNow() - _lastStats > StatsInterval)
        {
            _lastStats = time.GetUtcNow();
            var status = await OutboxStatusQuery.ReadAsync(db, _lastStats, cancellationToken);
            metrics.OutboxState(status.Pending, status.ParkedLastWeek);
        }

        return messages.Count;
    }

    private async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (!MessageTypes.TryGetValue(message.Type, out var type))
        {
            throw new InvalidOperationException($"Unknown outbox message type '{message.Type}'.");
        }

        var payload = JsonSerializer.Deserialize(message.Payload, type)!;

        // Each message runs in its own scope (own DbContext), separate from the claiming transaction.
        await using var scope = scopes.CreateAsyncScope();
        var handlerType = typeof(IOutboxMessageHandler<>).MakeGenericType(type);
        var handler = scope.ServiceProvider.GetRequiredService(handlerType);
        var handle = handlerType.GetMethod(nameof(IOutboxMessageHandler<IOutboxMessage>.HandleAsync))!;
        await (Task)handle.Invoke(handler, [payload, cancellationToken])!;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({Type}) failed, attempt {Attempt}; retrying in {DelaySeconds}s")]
    private static partial void LogRetry(ILogger logger, Guid messageId, string type, int attempt, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox message {MessageId} ({Type}) parked after too many attempts")]
    private static partial void LogParked(ILogger logger, Guid messageId, string type, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox batch failed")]
    private static partial void LogBatchFailed(ILogger logger, Exception exception);
}
