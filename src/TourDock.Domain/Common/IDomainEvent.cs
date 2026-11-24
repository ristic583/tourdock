namespace TourDock.Domain.Common;

/// <summary>
/// Something that happened to an aggregate that other parts of the system may react to (e.g. send an email).
/// Raised by aggregate methods; turned into outbox messages in the same transaction that saves the change.
/// </summary>
public interface IDomainEvent;
