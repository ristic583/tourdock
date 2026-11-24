using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Common;
using TourDock.Domain.Common;

namespace TourDock.Application.Tours;

public sealed record GetAgencyTourQuery(Guid AgencyId, Guid TourId) : IAgencyRequest, IQuery<TourResponse>;

internal sealed class GetAgencyTourHandler(
    IAgencyAccess access, ITourRepository tours, IPhotoUrls urls)
    : IQueryHandler<GetAgencyTourQuery, TourResponse>
{
    public async Task<Result<TourResponse>> Handle(GetAgencyTourQuery query, CancellationToken cancellationToken)
    {
        var agency = await access.RequireAsync(query.AgencyId, AgencyAccessLevel.Read, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        var tour = await tours.GetAsync(query.AgencyId, query.TourId, cancellationToken);
        return tour is null ? TourUseCaseErrors.NotFound : tour.ToResponse(urls);
    }
}
