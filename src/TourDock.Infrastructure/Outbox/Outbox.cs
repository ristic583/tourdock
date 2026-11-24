using Microsoft.EntityFrameworkCore;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Payments;
using TourDock.Infrastructure.Persistence;

namespace TourDock.Infrastructure.Outbox;

internal sealed class EfOutbox(TourDockDbContext db, TimeProvider time) : IOutbox
{
    public void Enqueue(IOutboxMessage message, DateTimeOffset? notBefore = null)
    {
        var entry = OutboxMessage.Create(message, time.GetUtcNow());
        if (notBefore > entry.NextAttemptAt)
        {
            entry.NextAttemptAt = notBefore.Value;
        }

        db.OutboxMessages.Add(entry);
    }
}

internal sealed class ProcessedEventStore(TourDockDbContext db, TimeProvider time) : IProcessedEventStore
{
    public Task<bool> WasProcessedAsync(string source, string eventId, CancellationToken cancellationToken) =>
        db.ProcessedEvents.AsNoTracking().AnyAsync(e => e.Source == source && e.EventId == eventId, cancellationToken);

    public async Task<bool> TryRecordAsync(string source, string eventId, CancellationToken cancellationToken)
    {
        if (await db.ProcessedEvents.AnyAsync(e => e.Source == source && e.EventId == eventId, cancellationToken))
        {
            return false;
        }

        // If the same event is being handled concurrently, the primary key makes one of the saves fail.
        db.ProcessedEvents.Add(new ProcessedEvent { Source = source, EventId = eventId, ProcessedAt = time.GetUtcNow() });
        return true;
    }
}

internal sealed class SentNotificationLog(TourDockDbContext db, TimeProvider time) : TourDock.Application.Abstractions.ISentNotificationLog
{
    public Task<bool> WasSentAsync(string key, CancellationToken cancellationToken) =>
        db.SentNotifications.AnyAsync(n => n.Key == key, cancellationToken);

    public void Record(string key, TourDock.Application.Abstractions.EmailDelivery delivery) =>
        db.SentNotifications.Add(new SentNotification { Key = key, Delivery = delivery.ToString(), SentAt = time.GetUtcNow() });
}
