using TourDock.Domain.Tours;

namespace TourDock.Domain.Slots;

/// <summary>
/// Turns a schedule rule into concrete departures. Start times are interpreted in the tour's
/// local time zone, so "09:00" stays 09:00 local across daylight-saving changes.
/// </summary>
public static class SlotGenerator
{
    public static IReadOnlyList<Slot> Generate(
        Tour tour, ScheduleRule rule, int vehicleCapacity, DateOnly from, DateOnly to, DateTimeOffset now)
    {
        var timeZone = tour.Location.TimeZone;
        var slots = new List<Slot>();

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (!rule.AppliesTo(date))
            {
                continue;
            }

            var startsAt = ToUtc(date.ToDateTime(rule.StartTime), timeZone);
            var slot = Slot.Create(tour, rule.VehicleId, vehicleCapacity, startsAt, now, rule.Id);
            if (slot.IsSuccess)
            {
                slots.Add(slot.Value);
            }
        }

        return slots;
    }

    /// <summary>
    /// Converts a local wall-clock time to UTC, handling DST edge cases:
    /// a time skipped by spring-forward moves one hour later; an ambiguous time during fall-back
    /// uses its first occurrence (the daylight offset).
    /// </summary>
    public static DateTimeOffset ToUtc(DateTime localTime, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        var offset = timeZone.IsAmbiguousTime(local)
            ? timeZone.GetAmbiguousTimeOffsets(local).Max()
            : timeZone.GetUtcOffset(local);

        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
