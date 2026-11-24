using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Domain.Administration;

namespace TourDock.Application.Common;

/// <summary>Adds an entry for the current admin to the audit log; it is saved with the change it describes.</summary>
internal sealed class AdminAudit(IUserContext user, IAdminAuditLog log, TimeProvider time)
{
    public void Record(string action, string targetType, Guid targetId, string? reason) =>
        log.Add(AdminAction.Record(user.UserId, action, targetType, targetId, reason, time.GetUtcNow()));
}
