using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Domain.Administration;
using TourDock.Domain.Common;

namespace TourDock.Application.Reviews;

public sealed record DismissReviewReportsCommand(Guid ReviewId) : ICommand<Unit>;

internal sealed class DismissReviewReportsHandler(ReviewModeration moderation) : ICommandHandler<DismissReviewReportsCommand, Unit>
{
    public Task<Result<Unit>> Handle(DismissReviewReportsCommand command, CancellationToken cancellationToken) =>
        moderation.RunAsync(command.ReviewId, AdminActions.ReviewDismissReports, null, (_, reports, now) =>
        {
            foreach (var report in reports)
            {
                report.Resolve(now);
            }

            return Result.Success();
        }, cancellationToken);
}
