using System.ComponentModel.DataAnnotations.Schema;

namespace TourDock.Domain.Common;

public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected Entity(Guid id) => Id = id;

    // Required by EF Core
    protected Entity() { }

    public Guid Id { get; private init; }

    /// <summary>Events raised since the entity was loaded or last saved. Not persisted.</summary>
    [NotMapped]
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public override bool Equals(object? obj) =>
        obj is Entity other && other.GetType() == GetType() && other.Id == Id;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    protected static Guid NewId() => Guid.CreateVersion7();
}
