using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Application.Tours;
using TourDock.Domain.Common;

namespace TourDock.Application.Photos;

public sealed record SetCoverPhotoCommand(Guid AgencyId, Guid TourId, Guid PhotoId) : IAgencyRequest, ICommand<TourResponse>,
    IInvalidatesCatalog;

internal sealed class SetCoverPhotoHandler(TourEditor editor) : ICommandHandler<SetCoverPhotoCommand, TourResponse>
{
    public Task<Result<TourResponse>> Handle(SetCoverPhotoCommand command, CancellationToken cancellationToken) =>
        editor.EditAsync(command.AgencyId, command.TourId, (tour, _) => tour.SetCoverPhoto(command.PhotoId), cancellationToken);
}
