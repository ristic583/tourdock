using FluentValidation;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Domain.Common;

namespace TourDock.Application.Reviews;

public sealed record GetAgencyReviewsQuery(Guid AgencyId, int Page, int PageSize) : IAgencyRequest, IQuery<PagedResponse<AgencyReview>>;

internal sealed class GetAgencyReviewsValidator : AbstractValidator<GetAgencyReviewsQuery>
{
    public GetAgencyReviewsValidator()
    {
        RuleFor(q => q.Page).InclusiveBetween(1, 1000);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}

internal sealed class GetAgencyReviewsHandler(
    IAgencyAccess access, IReviewReader reader)
    : IQueryHandler<GetAgencyReviewsQuery, PagedResponse<AgencyReview>>
{
    public async Task<Result<PagedResponse<AgencyReview>>> Handle(GetAgencyReviewsQuery query, CancellationToken cancellationToken)
    {
        var agency = await access.RequireAsync(query.AgencyId, AgencyAccessLevel.Read, cancellationToken);
        return agency.IsFailure ? agency.Error : await reader.GetForAgencyAsync(query.AgencyId, query.Page, query.PageSize,
            cancellationToken);
    }
}
