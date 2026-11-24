namespace TourDock.Domain.Common;

/// <summary>Timestamps are set by the persistence layer, never by domain code.</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; }
    DateTimeOffset UpdatedAt { get; }
}

/// <summary>Soft-deleted rows stay in the database (history, references) but are hidden from normal queries.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }
    DateTimeOffset? DeletedAt { get; }
}
