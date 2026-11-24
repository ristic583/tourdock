using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Common;
using TourDock.Domain.Common;

namespace TourDock.Application.Vehicles;

public sealed record DeleteVehicleCommand(Guid AgencyId, Guid VehicleId) : IAgencyRequest, ICommand<Unit>;

internal sealed class DeleteVehicleHandler(IAgencyAccess access, IVehicleRepository vehicles, IUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<DeleteVehicleCommand, Unit>
{
    public async Task<Result<Unit>> Handle(DeleteVehicleCommand command, CancellationToken cancellationToken)
    {
        var agency = await access.RequireAsync(command.AgencyId, AgencyAccessLevel.Manage, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        var vehicle = await vehicles.GetAsync(command.AgencyId, command.VehicleId, cancellationToken);
        if (vehicle is null)
        {
            return VehicleUseCaseErrors.NotFound;
        }

        var now = time.GetUtcNow();
        if (await vehicles.IsInUseAsync(vehicle.Id, now, cancellationToken))
        {
            return VehicleUseCaseErrors.InUse;
        }

        vehicle.Delete(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
