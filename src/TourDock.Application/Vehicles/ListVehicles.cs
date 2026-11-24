using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Common;
using TourDock.Domain.Common;

namespace TourDock.Application.Vehicles;

public sealed record ListVehiclesQuery(Guid AgencyId) : IAgencyRequest, IQuery<IReadOnlyList<VehicleResponse>>;

internal sealed class ListVehiclesHandler(IAgencyAccess access, IVehicleRepository vehicles)
    : IQueryHandler<ListVehiclesQuery, IReadOnlyList<VehicleResponse>>
{
    public async Task<Result<IReadOnlyList<VehicleResponse>>> Handle(ListVehiclesQuery query, CancellationToken cancellationToken)
    {
        var agency = await access.RequireAsync(query.AgencyId, AgencyAccessLevel.Read, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        return (await vehicles.ListAsync(query.AgencyId, cancellationToken)).Select(v => v.ToResponse()).ToList();
    }
}
