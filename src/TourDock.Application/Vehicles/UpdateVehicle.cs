using FluentValidation;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Common;
using TourDock.Domain.Common;
using TourDock.Domain.Vehicles;

namespace TourDock.Application.Vehicles;

public sealed record UpdateVehicleCommand(
    Guid AgencyId, Guid VehicleId, string Name, VehicleType Type, int Capacity)
    : IAgencyRequest, ICommand<VehicleResponse>;

internal sealed class UpdateVehicleValidator : AbstractValidator<UpdateVehicleCommand>
{
    public UpdateVehicleValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Vehicle.NameMaxLength);
        RuleFor(c => c.Type).IsInEnum();
        RuleFor(c => c.Capacity).InclusiveBetween(1, Vehicle.MaxCapacity);
    }
}

internal sealed class UpdateVehicleHandler(IAgencyAccess access, IVehicleRepository vehicles, IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateVehicleCommand, VehicleResponse>
{
    public async Task<Result<VehicleResponse>> Handle(UpdateVehicleCommand command, CancellationToken cancellationToken)
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

        var updated = vehicle.Update(command.Name, command.Type, command.Capacity);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return vehicle.ToResponse();
    }
}
