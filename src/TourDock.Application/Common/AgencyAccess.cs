using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Domain.Agencies;
using TourDock.Domain.Common;

namespace TourDock.Application.Common;

public enum AgencyAccessLevel
{
    /// <summary>Any member of the agency, and admins (read-only support).</summary>
    Read,

    /// <summary>Managers only.</summary>
    Manage,
}

/// <summary>
/// A request that acts on one agency's data. The access decorators check it before validation and the handler runs:
/// commands need <see cref="AgencyAccessLevel.Manage"/>, queries <see cref="AgencyAccessLevel.Read"/>. Outsiders get 404
/// whatever they send, so even a malformed request doesn't reveal that the agency exists.
/// </summary>
public interface IAgencyRequest
{
    Guid AgencyId { get; }
}

/// <summary>Tenant authorization: may the current user act on this agency?</summary>
public interface IAgencyAccess
{
    Task<Result<Agency>> RequireAsync(Guid agencyId, AgencyAccessLevel level, CancellationToken cancellationToken);
}

internal sealed class AgencyAccess(IUserContext user, IAgencyRepository agencies) : IAgencyAccess
{
    // Non-members get "not found", not "forbidden": they shouldn't learn that the agency exists.
    public static readonly Error NotFound = Error.NotFound("Agency.NotFound", "Agency not found.");

    public static readonly Error ManagersOnly = Error.Forbidden("Agency.ManagersOnly", "Only agency managers can do this.");

    public async Task<Result<Agency>> RequireAsync(Guid agencyId, AgencyAccessLevel level, CancellationToken cancellationToken)
    {
        var agency = await agencies.GetByIdAsync(agencyId, cancellationToken);
        if (agency is null)
        {
            return NotFound;
        }

        if (level == AgencyAccessLevel.Read && user.IsAdmin)
        {
            return agency;
        }

        if (!agency.HasMember(user.UserId))
        {
            return NotFound;
        }

        if (level == AgencyAccessLevel.Manage && !agency.IsManager(user.UserId))
        {
            return ManagersOnly;
        }

        return agency.Status == AgencyStatus.Suspended && level == AgencyAccessLevel.Manage
            ? AgencyErrors.NotActive
            : agency;
    }
}
