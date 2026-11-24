using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Domain.Administration;
using TourDock.Domain.Common;
using TourDock.Domain.Reviews;

namespace TourDock.Application.Reviews;

/// <summary>Admin moderation: every action is written to the audit log in the same save.</summary>
internal sealed class ReviewModeration(
    IUserContext user, IReviewRepository reviews, IAdminAuditLog audit, IUnitOfWork unitOfWork, TimeProvider time)
{
    public async Task<Result<Unit>> RunAsync(Guid reviewId, string action, string? reason, Func<Review, IReadOnlyList<ReviewReport>,
        DateTimeOffset, Result> change, CancellationToken cancellationToken)
    {
        var review = await reviews.GetByIdAsync(reviewId, cancellationToken);
        if (review is null)
        {
            return ReviewErrors.NotFound;
        }

        var now = time.GetUtcNow();
        var result = change(review, await reviews.GetOpenReportsAsync(reviewId, cancellationToken), now);
        if (result.IsFailure)
        {
            return result.Error;
        }

        audit.Add(AdminAction.Record(user.UserId, action, nameof(Review), review.Id, reason, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
