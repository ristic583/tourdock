using FluentValidation;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Domain.Common;

namespace TourDock.Application.Reviews;

public sealed record GetReviewsForModerationQuery(
    bool? Reported, bool? Hidden, int Page, int PageSize)
    : IQuery<PagedResponse<ModerationReview>>;

internal sealed class GetReviewsForModerationValidator : AbstractValidator<GetReviewsForModerationQuery>
{
    public GetReviewsForModerationValidator()
    {
        RuleFor(q => q.Page).InclusiveBetween(1, 1000);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}

internal sealed class GetReviewsForModerationHandler(
    IReviewReader reader)
    : IQueryHandler<GetReviewsForModerationQuery, PagedResponse<ModerationReview>>
{
    public async Task<Result<PagedResponse<ModerationReview>>> Handle(
        GetReviewsForModerationQuery q, CancellationToken cancellationToken) =>
        await reader.GetForModerationAsync(q.Reported, q.Hidden, q.Page, q.PageSize, cancellationToken);
}
