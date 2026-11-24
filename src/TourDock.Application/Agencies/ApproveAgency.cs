using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Domain.Agencies;
using TourDock.Domain.Common;

namespace TourDock.Application.Agencies;

public sealed record ApproveAgencyCommand(Guid AgencyId) : ICommand<AgencyResponse>, IInvalidatesCatalog;

internal sealed class ApproveAgencyHandler(IAgencyRepository agencies, IUnitOfWork unitOfWork, AdminAudit audit)
    : ICommandHandler<ApproveAgencyCommand, AgencyResponse>
{
    public Task<Result<AgencyResponse>> Handle(ApproveAgencyCommand command, CancellationToken cancellationToken) =>
        AgencyAdmin.ChangeAsync(agencies, unitOfWork, command.AgencyId, a => a.Approve(),
            a => audit.Record(Reviews.AdminActions.AgencyApprove, nameof(Agency), a.Id, null), cancellationToken);
}
