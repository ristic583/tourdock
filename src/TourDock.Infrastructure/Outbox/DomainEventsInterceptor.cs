using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TourDock.Application.Notifications;
using TourDock.Domain.Common;

namespace TourDock.Infrastructure.Outbox;

/// <summary>
/// Turns domain events raised by tracked aggregates into outbox messages right before saving, so the change and its
/// consequences (e.g. "send the confirmation email") are committed together or not at all. No handler has to remember
/// to enqueue anything.
/// </summary>
public sealed class DomainEventsInterceptor(IDomainEventOutboxMapper mapper, TimeProvider time, Application.Observability.TourDockMetrics metrics) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Collect(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        metrics.BookingsCreated(context.ChangeTracker.Entries<Domain.Bookings.Booking>().Count(e => e.State == EntityState.Added));

        var entities = context.ChangeTracker.Entries<Entity>()
            .Select(entry => entry.Entity)
            .Where(entity => entity.DomainEvents.Count > 0)
            .ToList();

        var now = time.GetUtcNow();
        foreach (var entity in entities)
        {
            foreach (var domainEvent in entity.DomainEvents)
            {
                metrics.Record(domainEvent);
                if (mapper.Map(domainEvent) is { } message)
                {
                    context.Set<OutboxMessage>().Add(OutboxMessage.Create(message, now));
                }
            }

            // Cleared here: if this save fails, the retry reloads the aggregate and raises the events again.
            entity.ClearDomainEvents();
        }
    }
}
