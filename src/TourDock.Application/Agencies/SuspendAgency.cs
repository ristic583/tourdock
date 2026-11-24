using FluentValidation;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Domain.Agencies;
using TourDock.Domain.Common;

namespace TourDock.Application.Agencies;

public sealed record SuspendAgencyCommand(Guid AgencyId, string Reason) : ICommand<AgencyResponse>, IInvalidatesCatalog;

internal sealed class SuspendAgencyValidator : AbstractValidator<SuspendAgencyCommand>
{
    public SuspendAgencyValidator() => RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class SuspendAgencyHandler(IAgencyRepository agencies, IUnitOfWork unitOfWork, AdminAudit audit)
    : ICommandHandler<SuspendAgencyCommand, AgencyResponse>
{
    public Task<Result<AgencyResponse>> Handle(SuspendAgencyCommand command, CancellationToken cancellationToken) =>
        AgencyAdmin.ChangeAsync(agencies, unitOfWork, command.AgencyId, a => a.Suspend(command.Reason),
            a => audit.Record(Reviews.AdminActions.AgencySuspend, nameof(Agency), a.Id, command.Reason), cancellationToken);
}
