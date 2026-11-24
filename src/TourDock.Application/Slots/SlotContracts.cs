using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Application.Tours;
using TourDock.Domain.Common;
using TourDock.Domain.Slots;
using TourDock.Domain.Tours;

namespace TourDock.Application.Slots;

public sealed record SlotResponse(
    Guid Id,
    Guid TourId,
    Guid VehicleId,
    Guid? ScheduleRuleId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateOnly LocalDate,
    int Capacity,
    int ReservedSeats,
    int RemainingSeats,
    BookingMode BookingMode,
    bool IsPrivatelyBooked,
    SlotStatus Status);

public sealed record ScheduleRuleCreatedResponse(ScheduleRuleDto Rule, int DeparturesCreated, int DeparturesSkippedForConflicts);

public sealed record GenerateSlotsResult(int DeparturesCreated, int DeparturesSkippedForConflicts);

public static class SlotUseCaseErrors
{
    public static readonly Error NotFound = Error.NotFound("Slot.NotFound", "Departure not found.");

    public static readonly Error VehicleBusy =
        Error.Conflict("Slot.VehicleBusy", "The vehicle already has a departure that overlaps this time.");

}

/// <summary>Materializes departures from schedule rules, skipping ones that already exist or would double-book the vehicle.</summary>
/// <summary>How far ahead departures exist and can be booked: a year, so summer trips can be booked in winter.</summary>
public static class BookingHorizon
{
    public const int Days = 365;
}

internal static class SlotMapping
{
    public static SlotResponse ToResponse(this Slot s) => new(
        s.Id, s.TourId, s.VehicleId, s.ScheduleRuleId, s.StartsAt, s.EndsAt, s.LocalDate, s.Capacity, s.ReservedSeats,
        s.RemainingSeats, s.BookingMode, s.IsPrivatelyBooked, s.Status);
}
