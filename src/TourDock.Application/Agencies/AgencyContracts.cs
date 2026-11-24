using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Common;
using TourDock.Domain.Agencies;
using TourDock.Domain.Common;

namespace TourDock.Application.Agencies;

public sealed record AgencyResponse(Guid Id, string Name, string ContactEmail, string Status, string? MyRole);

public sealed record AdminAgencyResponse(
    Guid Id, string Name, string ContactEmail, AgencyStatus Status, string? SuspensionReason, DateTimeOffset CreatedAt, int ManagerCount);

internal static class AgencyAdmin
{
    public static readonly Error NotFound = Error.NotFound("Agency.NotFound", "Agency not found.");

    public static async Task<Result<AgencyResponse>> ChangeAsync(
        IAgencyRepository agencies, IUnitOfWork unitOfWork, Guid agencyId, Func<Agency, Result> change, Action<Agency> audit,
        CancellationToken cancellationToken)
    {
        var agency = await agencies.GetByIdAsync(agencyId, cancellationToken);
        if (agency is null)
        {
            return NotFound;
        }

        var result = change(agency);
        if (result.IsFailure)
        {
            return result.Error;
        }

        audit(agency); // saved together with the change
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return agency.ToResponse(null);
    }
}

internal static class AgencyMapping
{
    public static AgencyResponse ToResponse(this Agency agency, Guid? userId) => new(
        agency.Id,
        agency.Name,
        agency.ContactEmail,
        agency.Status.ToString(),
        agency.Members.SingleOrDefault(m => m.UserId == userId)?.Role.ToString());
}
