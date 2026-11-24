using FluentValidation;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Common;
using TourDock.Domain.Common;
using TourDock.Domain.Vehicles;

namespace TourDock.Application.Vehicles;

public sealed record CreateVehicleCommand(Guid AgencyId, string Name, VehicleType Type, int Capacity) : IAgencyRequest,
    ICommand<VehicleResponse>;

internal sealed class CreateVehicleValidator : AbstractValidator<CreateVehicleCommand>
{
    public CreateVehicleValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Vehicle.NameMaxLength);
        RuleFor(c => c.Type).IsInEnum();
        RuleFor(c => c.Capacity).InclusiveBetween(1, Vehicle.MaxCapacity);
    }
}

internal sealed class CreateVehicleHandler(IAgencyAccess access, IVehicleRepository vehicles, IUnitOfWork unitOfWork)
    : ICommandHandler<CreateVehicleCommand, VehicleResponse>
{
    public async Task<Result<VehicleResponse>> Handle(CreateVehicleCommand command, CancellationToken cancellationToken)
    {
        var agency = await access.RequireAsync(command.AgencyId, AgencyAccessLevel.Manage, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        var vehicle = Vehicle.Create(command.AgencyId, command.Name, command.Type, command.Capacity);
        if (vehicle.IsFailure)
        {
            return vehicle.Error;
        }

        vehicles.Add(vehicle.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return vehicle.Value.ToResponse();
    }
}
