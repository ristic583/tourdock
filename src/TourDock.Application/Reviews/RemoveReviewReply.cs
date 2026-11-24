using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Common;
using TourDock.Domain.Common;
using TourDock.Domain.Reviews;

namespace TourDock.Application.Reviews;

public sealed record RemoveReviewReplyCommand(Guid AgencyId, Guid ReviewId) : IAgencyRequest, ICommand<Unit>;

internal sealed class RemoveReviewReplyHandler(IAgencyAccess access, IReviewRepository reviews, IUnitOfWork unitOfWork)
    : ICommandHandler<RemoveReviewReplyCommand, Unit>
{
    public async Task<Result<Unit>> Handle(RemoveReviewReplyCommand command, CancellationToken cancellationToken)
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

        review.RemoveReply();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
