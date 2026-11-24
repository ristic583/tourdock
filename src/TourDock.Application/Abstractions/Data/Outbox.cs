namespace TourDock.Application.Abstractions.Data;

/// <summary>Marker for messages that are stored in the outbox and handled later by a background worker.</summary>
public interface IOutboxMessage;

/// <summary>
/// Transactional outbox: messages are saved in the same transaction as the business change, then delivered
/// by a background worker with retries. Either both happen or neither; nothing is lost if a call to the outside fails.
/// </summary>
public interface IOutbox
{
    /// <param name="notBefore">Deliver no earlier than this (e.g. a delayed retry); default: as soon as possible.</param>
    void Enqueue(IOutboxMessage message, DateTimeOffset? notBefore = null);
}

/// <summary>Handles one outbox message. Throwing means "retry later" (with backoff).</summary>
public interface IOutboxMessageHandler<in TMessage>
    where TMessage : IOutboxMessage
{
    Task HandleAsync(TMessage message, CancellationToken cancellationToken);
}
