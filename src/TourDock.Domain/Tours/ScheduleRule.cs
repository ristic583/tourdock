using TourDock.Domain.Common;

namespace TourDock.Domain.Tours;

/// <summary>
/// "Every Mon/Wed/Sat at 09:00 (tour-local time) between ValidFrom and ValidUntil, using this vehicle."
/// Departures (slots) are generated from active rules ahead of time.
/// </summary>
public sealed class ScheduleRule : Entity, IAuditable
{
    internal ScheduleRule(Guid tourId, Guid vehicleId, DaysOfWeek days, TimeOnly startTime, DateOnly validFrom, DateOnly? validUntil)
        : base(NewId())
    {
        TourId = tourId;
        VehicleId = vehicleId;
        Days = days;
        StartTime = startTime;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
        IsActive = true;
    }

    private ScheduleRule() { }

    public Guid TourId { get; private set; }

    public Guid VehicleId { get; private set; }

    public DaysOfWeek Days { get; private set; }

    /// <summary>Local time at the tour's location.</summary>
    public TimeOnly StartTime { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    public DateOnly? ValidUntil { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool AppliesTo(DateOnly date) =>
        IsActive
        && date >= ValidFrom
        && (ValidUntil is null || date <= ValidUntil)
        && Days.HasFlag(date.DayOfWeek.ToFlag());

    /// <remarks>Deactivating stops generating new departures. Existing departures (and their bookings) are untouched.</remarks>
    internal void Deactivate() => IsActive = false;
}

[Flags]
public enum DaysOfWeek
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
}

public static class DaysOfWeekExtensions
{
    public static DaysOfWeek ToFlag(this DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => DaysOfWeek.Monday,
        DayOfWeek.Tuesday => DaysOfWeek.Tuesday,
        DayOfWeek.Wednesday => DaysOfWeek.Wednesday,
        DayOfWeek.Thursday => DaysOfWeek.Thursday,
        DayOfWeek.Friday => DaysOfWeek.Friday,
        DayOfWeek.Saturday => DaysOfWeek.Saturday,
        DayOfWeek.Sunday => DaysOfWeek.Sunday,
        _ => throw new ArgumentOutOfRangeException(nameof(day)),
    };
}
