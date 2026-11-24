using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Domain.Common;

namespace TourDock.Application.Reviews;

public sealed record UnhideReviewCommand(Guid ReviewId) : ICommand<Unit>, IInvalidatesCatalog;

internal sealed class UnhideReviewHandler(ReviewModeration moderation) : ICommandHandler<UnhideReviewCommand, Unit>
{
    public Task<Result<Unit>> Handle(UnhideReviewCommand command, CancellationToken cancellationToken) =>
        moderation.RunAsync(command.ReviewId, AdminActions.ReviewUnhide, null, (review, _, _) =>
        {
            review.Unhide();
            return Result.Success();
        }, cancellationToken);
}
