using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Domain.Common;

namespace TourDock.Application.Tours;

public sealed record UnpublishTourCommand(Guid AgencyId, Guid TourId) : IAgencyRequest, ICommand<TourResponse>, IInvalidatesCatalog;

internal sealed class UnpublishTourHandler(TourEditor editor) : ICommandHandler<UnpublishTourCommand, TourResponse>
{
    public Task<Result<TourResponse>> Handle(UnpublishTourCommand command, CancellationToken cancellationToken) =>
        editor.EditAsync(command.AgencyId, command.TourId, (tour, _) =>
        {
            tour.Unpublish();
            return Result.Success();
        }, cancellationToken);
}
