using FluentValidation;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Domain.Agencies;
using TourDock.Domain.Common;

namespace TourDock.Application.Agencies;

/// <summary>Admin only (enforced by the endpoint's authorization policy).</summary>
public sealed record ListAgenciesQuery(AgencyStatus? Status, int Page, int PageSize) : IQuery<Catalog.PagedResponse<AdminAgencyResponse>>;

internal sealed class ListAgenciesValidator : AbstractValidator<ListAgenciesQuery>
{
    public ListAgenciesValidator()
    {
        RuleFor(q => q.Status).IsInEnum();
        RuleFor(q => q.Page).InclusiveBetween(1, 1000);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}

internal sealed class ListAgenciesHandler(
    IAgencyRepository agencies)
    : IQueryHandler<ListAgenciesQuery, Catalog.PagedResponse<AdminAgencyResponse>>
{
    public async Task<Result<Catalog.PagedResponse<AdminAgencyResponse>>> Handle(
        ListAgenciesQuery query, CancellationToken cancellationToken)
    {
        var (items, total) = await agencies.ListAsync(query.Status, query.Page, query.PageSize, cancellationToken);
        var rows = items.Select(a => new AdminAgencyResponse(
            a.Id, a.Name, a.ContactEmail, a.Status, a.SuspensionReason, a.CreatedAt,
            a.Members.Count(m => m.Role == AgencyRole.Manager))).ToList();
        return new Catalog.PagedResponse<AdminAgencyResponse>(rows, query.Page, query.PageSize, total);
    }
}
