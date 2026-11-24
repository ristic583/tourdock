using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Domain.Common;
using TourDock.Domain.Reviews;

namespace TourDock.Application.Reviews;

public sealed record DeleteReviewCommand(Guid BookingId) : ICommand<Unit>, IInvalidatesCatalog;

internal sealed class DeleteReviewHandler(
    IUserContext user, IReviewRepository reviews, IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteReviewCommand, Unit>
{
    public async Task<Result<Unit>> Handle(DeleteReviewCommand command, CancellationToken cancellationToken)
    {
        var review = await reviews.GetByBookingIdAsync(command.BookingId, cancellationToken);
        if (review is null || review.CustomerId != user.UserId)
        {
            return ReviewErrors.NotFound;
        }

        reviews.Remove(review);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
