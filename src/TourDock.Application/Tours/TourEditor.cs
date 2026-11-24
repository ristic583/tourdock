using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Common;
using TourDock.Domain.Agencies;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Tours;

/// <summary>Shared flow for tour edits: check manager access, load the aggregate, apply the change, save.</summary>
internal sealed class TourEditor(IAgencyAccess access, ITourRepository tours, IUnitOfWork unitOfWork, IPhotoUrls urls)
{
    public async Task<Result<TourResponse>> EditAsync(
        Guid agencyId, Guid tourId, Func<Tour, Agency, Result> change, CancellationToken cancellationToken)
    {
        var agency = await access.RequireAsync(agencyId, AgencyAccessLevel.Manage, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        var tour = await tours.GetAsync(agencyId, tourId, cancellationToken);
        if (tour is null)
        {
            return TourUseCaseErrors.NotFound;
        }

        var result = change(tour, agency.Value);
        if (result.IsFailure)
        {
            return result.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return tour.ToResponse(urls);
    }
}
