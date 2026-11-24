using TourDock.Domain.Common;

namespace TourDock.Domain.Vehicles;

public enum VehicleType
{
    Boat,
    Car,
    Jeep,
    Bus,
    Other,
}

/// <summary>A resource an agency uses to run tours. It can only be on one departure at a time.</summary>
public sealed class Vehicle : Entity, IAuditable, ISoftDeletable
{
    public const int NameMaxLength = 80;
    public const int MaxCapacity = 500;

    private Vehicle(Guid id, Guid agencyId, string name, VehicleType type, int capacity)
        : base(id)
    {
        AgencyId = agencyId;
        Name = name;
        Type = type;
        Capacity = capacity;
    }

    private Vehicle() { }

    public Guid AgencyId { get; private set; }

    public string Name { get; private set; } = null!;

    public VehicleType Type { get; private set; }

    /// <summary>Maximum number of guests the vehicle can carry.</summary>
    public int Capacity { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public static Result<Vehicle> Create(Guid agencyId, string name, VehicleType type, int capacity)
    {
        var validation = Validate(name, capacity);
        return validation.IsFailure
            ? validation.Error
            : new Vehicle(NewId(), agencyId, name.Trim(), type, capacity);
    }

    /// <remarks>Lowering capacity does not change existing departures; their capacity was fixed when they were created.</remarks>
    public Result Update(string name, VehicleType type, int capacity)
    {
        var validation = Validate(name, capacity);
        if (validation.IsFailure)
        {
            return validation;
        }

        Name = name.Trim();
        Type = type;
        Capacity = capacity;
        return Result.Success();
    }

    public void Delete(DateTimeOffset now)
    {
        IsDeleted = true;
        DeletedAt = now;
    }

    private static Result Validate(string name, int capacity)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > NameMaxLength)
        {
            return VehicleErrors.InvalidName;
        }

        return capacity is < 1 or > MaxCapacity ? VehicleErrors.InvalidCapacity : Result.Success();
    }
}

public static class VehicleErrors
{
    public static readonly Error InvalidName =
        Error.Validation("Vehicle.InvalidName", $"Name is required and must be at most {Vehicle.NameMaxLength} characters.");

    public static readonly Error InvalidCapacity =
        Error.Validation("Vehicle.InvalidCapacity", $"Capacity must be between 1 and {Vehicle.MaxCapacity}.");
}
