using FluentValidation;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Domain.Common;

namespace TourDock.Application.Reviews;

public sealed record GetTourReviewsQuery(Guid TourId, ReviewSort Sort, int Page, int PageSize) : IQuery<TourReviewsResponse>;

internal sealed class GetTourReviewsValidator : AbstractValidator<GetTourReviewsQuery>
{
    public GetTourReviewsValidator()
    {
        RuleFor(q => q.Sort).IsInEnum();
        RuleFor(q => q.Page).InclusiveBetween(1, 1000);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}

internal sealed class GetTourReviewsHandler(IReviewReader reader) : IQueryHandler<GetTourReviewsQuery, TourReviewsResponse>
{
    public async Task<Result<TourReviewsResponse>> Handle(GetTourReviewsQuery query, CancellationToken cancellationToken) =>
        await reader.GetForTourAsync(query.TourId, query.Sort, query.Page, query.PageSize,
            cancellationToken) is { } reviews ? reviews : CatalogErrors.TourNotFound;
}
