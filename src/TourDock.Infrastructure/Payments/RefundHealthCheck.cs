using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TourDock.Domain.Payments;
using TourDock.Infrastructure.Persistence;

namespace TourDock.Infrastructure.Payments;

/// <summary>
/// Degraded (still HTTP 200) while any refund has given up: a customer is owed money and an admin has to act
/// (GET /api/v1/admin/refunds). The API itself works, so the instance stays in rotation.
/// </summary>
internal sealed class RefundHealthCheck(TourDockDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var failed = await db.Refunds.AsNoTracking().CountAsync(r => r.Status == RefundStatus.Failed, cancellationToken);
        var data = new Dictionary<string, object> { ["failedRefunds"] = failed };
        return failed > 0
            ? HealthCheckResult.Degraded($"{failed} refund(s) failed and wait for an admin.", data: data)
            : HealthCheckResult.Healthy(data: data);
    }
}
