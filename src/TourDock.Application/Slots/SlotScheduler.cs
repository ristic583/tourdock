using TourDock.Application.Abstractions.Data;
using TourDock.Application.Common;
using TourDock.Application.Tours;
using TourDock.Application.Vehicles;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Slots;
using TourDock.Domain.Tours;

namespace TourDock.Application.Slots;

internal sealed class SlotScheduler(ISlotRepository slots, IVehicleRepository vehicles, TimeProvider time)
{
    public async Task<GenerateSlotsResult> GenerateAsync(Tour tour, ScheduleRule rule, CancellationToken cancellationToken)
    {
        var vehicle = await vehicles.GetAsync(tour.AgencyId, rule.VehicleId, cancellationToken);
        if (vehicle is null || !rule.IsActive || tour.IsDeleted)
        {
            return new GenerateSlotsResult(0, 0);
        }

        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, tour.Location.TimeZone).DateTime);
        var existing = await slots.GetRuleStartTimesAsync(rule.Id, now, cancellationToken);

        int created = 0, skipped = 0;
        foreach (var slot in SlotGenerator.Generate(tour, rule, vehicle.Capacity, today, today.AddDays(BookingHorizon.Days), now))
        {
            if (existing.Contains(slot.StartsAt))
            {
                continue;
            }

            if (await slots.IsVehicleBusyAsync(slot.VehicleId, slot.StartsAt, slot.EndsAt, cancellationToken))
            {
                skipped++;
                continue;
            }

            slots.Add(slot);
            created++;
        }

        return new GenerateSlotsResult(created, skipped);
    }
}
