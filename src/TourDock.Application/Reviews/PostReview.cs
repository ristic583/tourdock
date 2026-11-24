using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Domain.Common;
using TourDock.Domain.Reviews;

namespace TourDock.Application.Reviews;

public sealed record PostReviewCommand(Guid BookingId, int Rating, string? Comment) : ICommand<MyReview>, IInvalidatesCatalog;

internal sealed class PostReviewHandler(
    IUserContext user, IBookingRepository bookings, IReviewRepository reviews, IReviewReader reader, IUnitOfWork unitOfWork,
    TimeProvider time)
    : ICommandHandler<PostReviewCommand, MyReview>
{
    public async Task<Result<MyReview>> Handle(PostReviewCommand command, CancellationToken cancellationToken)
    {
        var booking = await bookings.GetByIdAsync(command.BookingId, cancellationToken);
        if (booking is null || booking.CustomerId != user.UserId)
        {
            return ReviewUseCaseErrors.BookingNotFound;
        }

        if (await reviews.GetByBookingIdAsync(booking.Id, cancellationToken) is not null)
        {
            return ReviewErrors.AlreadyReviewed;
        }

        var review = Review.Create(booking, user.UserId, command.Rating, command.Comment, time.GetUtcNow());
        if (review.IsFailure)
        {
            return review.Error;
        }

        reviews.Add(review.Value);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateKeyException)
        {
            // Two submissions at once (double click): the unique index on booking_id lets only one through.
            return ReviewErrors.AlreadyReviewed;
        }

        return (await reader.GetMineAsync(user.UserId, booking.Id, cancellationToken))!;
    }
}
