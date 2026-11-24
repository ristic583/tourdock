using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Application.Tours;
using TourDock.Domain.Common;

namespace TourDock.Application.Photos;

public sealed record ReorderPhotosCommand(
    Guid AgencyId, Guid TourId, IReadOnlyList<Guid> PhotoIds)
    : IAgencyRequest, ICommand<TourResponse>, IInvalidatesCatalog;

internal sealed class ReorderPhotosHandler(TourEditor editor) : ICommandHandler<ReorderPhotosCommand, TourResponse>
{
    public Task<Result<TourResponse>> Handle(ReorderPhotosCommand command, CancellationToken cancellationToken) =>
        editor.EditAsync(command.AgencyId, command.TourId, (tour, _) => tour.ReorderPhotos(command.PhotoIds), cancellationToken);
}
