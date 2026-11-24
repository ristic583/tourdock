namespace TourDock.Application.Abstractions.Data;

/// <summary>Another transaction changed the same row first (optimistic concurrency). Safe to reload and retry.</summary>
public sealed class ConcurrencyConflictException(Exception inner)
    : Exception("The data was changed by another request.", inner);

/// <summary>A unique constraint rejected the save.</summary>
public sealed class DuplicateKeyException(string? constraintName, Exception inner)
    : Exception($"Duplicate key ({constraintName}).", inner)
{
    public string? ConstraintName { get; } = constraintName;
}
