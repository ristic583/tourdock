using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Application.Tours;
using TourDock.Domain.Common;

namespace TourDock.Application.Photos;

public sealed record DeleteTourPhotoCommand(Guid AgencyId, Guid TourId, Guid PhotoId) : IAgencyRequest, ICommand<Unit>, IInvalidatesCatalog;

internal sealed class DeleteTourPhotoHandler(IAgencyAccess access, ITourRepository tours, IOutbox outbox, IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteTourPhotoCommand, Unit>
{
    public async Task<Result<Unit>> Handle(DeleteTourPhotoCommand command, CancellationToken cancellationToken)
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

        var removed = tour.RemovePhoto(command.PhotoId);
        if (removed.IsFailure)
        {
            return removed.Error;
        }

        // Files are deleted after the database change commits, reliably, through the outbox.
        outbox.Enqueue(new DeletePhotoFilesMessage(PhotoVariants.Keys(removed.Value.StorageKey)));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
