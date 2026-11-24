using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Common;
using TourDock.Domain.Common;
using TourDock.Domain.Vehicles;

namespace TourDock.Application.Vehicles;

public sealed record VehicleResponse(Guid Id, string Name, VehicleType Type, int Capacity);

public static class VehicleUseCaseErrors
{
    public static readonly Error NotFound = Error.NotFound("Vehicle.NotFound", "Vehicle not found.");

    public static readonly Error InUse =
        Error.Conflict("Vehicle.InUse", "The vehicle has upcoming departures or active schedules. Cancel or move them first.");
}

internal static class VehicleMapping
{
    public static VehicleResponse ToResponse(this Vehicle vehicle) => new(vehicle.Id, vehicle.Name, vehicle.Type, vehicle.Capacity);
}
