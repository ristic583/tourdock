using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Domain.Agencies;
using TourDock.Domain.Common;

namespace TourDock.Application.Tours;

public sealed record PublishTourCommand(Guid AgencyId, Guid TourId) : IAgencyRequest, ICommand<TourResponse>, IInvalidatesCatalog;

internal sealed class PublishTourHandler(TourEditor editor) : ICommandHandler<PublishTourCommand, TourResponse>
{
    public Task<Result<TourResponse>> Handle(PublishTourCommand command, CancellationToken cancellationToken) =>
        editor.EditAsync(command.AgencyId, command.TourId,
            (tour, agency) => agency.Status switch
            {
                AgencyStatus.Active => tour.Publish(),
                AgencyStatus.PendingApproval => AgencyErrors.NotApproved, // a state to wait for, not a permission problem
                _ => AgencyErrors.NotActive,
            },
            cancellationToken);
}
