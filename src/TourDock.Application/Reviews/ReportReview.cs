using FluentValidation;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Domain.Administration;
using TourDock.Domain.Common;
using TourDock.Domain.Reviews;

namespace TourDock.Application.Reviews;

public sealed record ReportReviewCommand(Guid ReviewId, ReportReason Reason, string? Note) : ICommand<Unit>;

internal sealed class ReportReviewValidator : AbstractValidator<ReportReviewCommand>
{
    public ReportReviewValidator() => RuleFor(c => c.Reason).IsInEnum();
}

internal sealed class ReportReviewHandler(IUserContext user, IReviewRepository reviews, IUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<ReportReviewCommand, Unit>
{
    public async Task<Result<Unit>> Handle(ReportReviewCommand command, CancellationToken cancellationToken)
    {
        var review = await reviews.GetByIdAsync(command.ReviewId, cancellationToken);
        if (review is null)
        {
            return ReviewErrors.NotFound;
        }

        // Reporting twice is not an error: the first report stands.
        if (await reviews.HasReportedAsync(review.Id, user.UserId, cancellationToken))
        {
            return Unit.Value;
        }

        var report = ReviewReport.Create(review, user.UserId, command.Reason, command.Note, time.GetUtcNow());
        if (report.IsFailure)
        {
            return report.Error;
        }

        reviews.AddReport(report.Value);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateKeyException)
        {
            // A concurrent duplicate from the same user: same outcome.
        }

        return Unit.Value;
    }
}
