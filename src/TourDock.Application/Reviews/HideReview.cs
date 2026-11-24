using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Domain.Common;

namespace TourDock.Application.Reviews;

public sealed record HideReviewCommand(Guid ReviewId, string Reason) : ICommand<Unit>, IInvalidatesCatalog;

internal sealed class HideReviewHandler(ReviewModeration moderation) : ICommandHandler<HideReviewCommand, Unit>
{
    public Task<Result<Unit>> Handle(HideReviewCommand command, CancellationToken cancellationToken) =>
        moderation.RunAsync(command.ReviewId, AdminActions.ReviewHide, command.Reason, (review, reports, now) =>
        {
            var hidden = review.Hide(command.Reason);
            if (hidden.IsSuccess)
            {
                foreach (var report in reports)
                {
                    report.Resolve(now);
                }
            }

            return hidden;
        }, cancellationToken);
}
