using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Domain.Agencies;
using TourDock.Domain.Common;

namespace TourDock.Application.Agencies;

public sealed record GetMyAgenciesQuery : IQuery<IReadOnlyList<AgencyResponse>>;

internal sealed class GetMyAgenciesHandler(IUserContext user, IAgencyRepository agencies)
    : IQueryHandler<GetMyAgenciesQuery, IReadOnlyList<AgencyResponse>>
{
    public async Task<Result<IReadOnlyList<AgencyResponse>>> Handle(GetMyAgenciesQuery query, CancellationToken cancellationToken)
    {
        var list = await agencies.GetForMemberAsync(user.UserId, cancellationToken);
        return list.Select(a => a.ToResponse(user.UserId)).ToList();
    }
}
