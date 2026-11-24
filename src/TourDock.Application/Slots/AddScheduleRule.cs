using FluentValidation;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Application.Tours;
using TourDock.Application.Vehicles;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Slots;

public sealed record AddScheduleRuleCommand(
    Guid AgencyId, Guid TourId, Guid VehicleId, DaysOfWeek Days, TimeOnly StartTime, DateOnly ValidFrom, DateOnly? ValidUntil)
    : IAgencyRequest, ICommand<ScheduleRuleCreatedResponse>, IInvalidatesCatalog;

internal sealed class AddScheduleRuleValidator : AbstractValidator<AddScheduleRuleCommand>
{
    public AddScheduleRuleValidator()
    {
        RuleFor(c => c.Days).NotEqual(DaysOfWeek.None).Must(d => (int)d is > 0 and < 128).WithMessage("Invalid days of week.");
        RuleFor(c => c.ValidUntil).GreaterThanOrEqualTo(c => c.ValidFrom).When(c => c.ValidUntil is not null);
    }
}

internal sealed class AddScheduleRuleHandler(
    IAgencyAccess access, ITourRepository tours, IVehicleRepository vehicles, SlotScheduler scheduler, IUnitOfWork unitOfWork)
    : ICommandHandler<AddScheduleRuleCommand, ScheduleRuleCreatedResponse>
{
    public async Task<Result<ScheduleRuleCreatedResponse>> Handle(AddScheduleRuleCommand command, CancellationToken cancellationToken)
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

        if (await vehicles.GetAsync(command.AgencyId, command.VehicleId, cancellationToken) is null)
        {
            return VehicleUseCaseErrors.NotFound;
        }

        var rule = tour.AddScheduleRule(command.VehicleId, command.Days, command.StartTime, command.ValidFrom, command.ValidUntil);
        if (rule.IsFailure)
        {
            return rule.Error;
        }

        var generated = await scheduler.GenerateAsync(tour, rule.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var r = rule.Value;
        return new ScheduleRuleCreatedResponse(
            new ScheduleRuleDto(r.Id, r.VehicleId, r.Days, r.StartTime, r.ValidFrom, r.ValidUntil, r.IsActive),
            generated.DeparturesCreated,
            generated.DeparturesSkippedForConflicts);
    }
}
