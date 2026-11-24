using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Application.Tours;
using TourDock.Application.Vehicles;
using TourDock.Domain.Common;
using TourDock.Domain.Slots;

namespace TourDock.Application.Slots;

/// <param name="LocalStart">Wall-clock start time at the tour's location (no offset).</param>
public sealed record CreateSlotCommand(
    Guid AgencyId, Guid TourId, Guid VehicleId, DateTime LocalStart)
    : IAgencyRequest, ICommand<SlotResponse>, IInvalidatesCatalog;

internal sealed class CreateSlotHandler(
    IAgencyAccess access, ITourRepository tours, IVehicleRepository vehicles, ISlotRepository slots, IUnitOfWork unitOfWork,
    TimeProvider time)
    : ICommandHandler<CreateSlotCommand, SlotResponse>
{
    public async Task<Result<SlotResponse>> Handle(CreateSlotCommand command, CancellationToken cancellationToken)
    {
        var agency = await access.RequireAsync(command.AgencyId, AgencyAccessLevel.Manage, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        var tour = await tours.GetAsync(command.AgencyId, command.TourId, cancellationToken);
        if (tour is null)
        {
            return TourUseCaseErrors.NotFound;
        }

        var vehicle = await vehicles.GetAsync(command.AgencyId, command.VehicleId, cancellationToken);
        if (vehicle is null)
        {
            return VehicleUseCaseErrors.NotFound;
        }

        var startsAt = SlotGenerator.ToUtc(command.LocalStart, tour.Location.TimeZone);
        var slot = Slot.Create(tour, vehicle.Id, vehicle.Capacity, startsAt, time.GetUtcNow());
        if (slot.IsFailure)
        {
            return slot.Error;
        }

        if (await slots.IsVehicleBusyAsync(vehicle.Id, slot.Value.StartsAt, slot.Value.EndsAt, cancellationToken))
        {
            return SlotUseCaseErrors.VehicleBusy;
        }

        slots.Add(slot.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return slot.Value.ToResponse();
    }
}
