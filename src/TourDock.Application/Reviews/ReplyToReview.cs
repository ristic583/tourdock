using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Common;
using TourDock.Domain.Common;
using TourDock.Domain.Reviews;

namespace TourDock.Application.Reviews;

public sealed record ReplyToReviewCommand(Guid AgencyId, Guid ReviewId, string Text) : IAgencyRequest, ICommand<AgencyReview>;

internal sealed class ReplyToReviewHandler(
    IAgencyAccess access, IReviewRepository reviews, IReviewReader reader, IUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<ReplyToReviewCommand, AgencyReview>
{
    public async Task<Result<AgencyReview>> Handle(ReplyToReviewCommand command, CancellationToken cancellationToken)
    {
        var agency = await access.RequireAsync(command.AgencyId, AgencyAccessLevel.Manage, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        var review = await reviews.GetByIdAsync(command.ReviewId, cancellationToken);
        if (review is null || review.AgencyId != command.AgencyId)
        {
            return ReviewErrors.NotFound;
        }

        var replied = review.Reply(command.Text, time.GetUtcNow());
        if (replied.IsFailure)
        {
            return replied.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await reader.GetOneForAgencyAsync(command.AgencyId, review.Id,
            cancellationToken) is { } updated ? updated : ReviewErrors.NotFound;
    }
}
