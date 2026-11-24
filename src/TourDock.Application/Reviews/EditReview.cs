using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Domain.Common;
using TourDock.Domain.Reviews;

namespace TourDock.Application.Reviews;

public sealed record EditReviewCommand(Guid BookingId, int Rating, string? Comment) : ICommand<MyReview>, IInvalidatesCatalog;

internal sealed class EditReviewHandler(
    IUserContext user, IReviewRepository reviews, IReviewReader reader, IUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<EditReviewCommand, MyReview>
{
    public async Task<Result<MyReview>> Handle(EditReviewCommand command, CancellationToken cancellationToken)
    {
        var review = await reviews.GetByBookingIdAsync(command.BookingId, cancellationToken);
        if (review is null || review.CustomerId != user.UserId)
        {
            return ReviewErrors.NotFound;
        }

        var edited = review.Edit(command.Rating, command.Comment, time.GetUtcNow());
        if (edited.IsFailure)
        {
            return edited.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (await reader.GetMineAsync(user.UserId, command.BookingId, cancellationToken))!;
    }
}
