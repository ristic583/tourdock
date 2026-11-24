using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Common;
using TourDock.Domain.Common;

namespace TourDock.Application.Tours;

public sealed record ListAgencyToursQuery(Guid AgencyId) : IAgencyRequest, IQuery<IReadOnlyList<TourResponse>>;

internal sealed class ListAgencyToursHandler(IAgencyAccess access, ITourRepository tours, IPhotoUrls urls)
    : IQueryHandler<ListAgencyToursQuery, IReadOnlyList<TourResponse>>
{
    public async Task<Result<IReadOnlyList<TourResponse>>> Handle(ListAgencyToursQuery query, CancellationToken cancellationToken)
    {
        var agency = await access.RequireAsync(query.AgencyId, AgencyAccessLevel.Read, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        return (await tours.ListAsync(query.AgencyId, cancellationToken)).Select(t => t.ToResponse(urls)).ToList();
    }
}
