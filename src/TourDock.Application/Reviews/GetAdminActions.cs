using FluentValidation;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Domain.Common;

namespace TourDock.Application.Reviews;

public sealed record GetAdminActionsQuery(int Page, int PageSize) : IQuery<PagedResponse<AdminActionResponse>>;

internal sealed class GetAdminActionsValidator : AbstractValidator<GetAdminActionsQuery>
{
    public GetAdminActionsValidator()
    {
        RuleFor(q => q.Page).InclusiveBetween(1, 1000);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}

internal sealed class GetAdminActionsHandler(IReviewReader reader) : IQueryHandler<GetAdminActionsQuery, PagedResponse<AdminActionResponse>>
{
    public async Task<Result<PagedResponse<AdminActionResponse>>> Handle(GetAdminActionsQuery query, CancellationToken cancellationToken) =>
        await reader.GetAdminActionsAsync(query.Page, query.PageSize, cancellationToken);
}
