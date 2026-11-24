using Microsoft.EntityFrameworkCore;
using TourDock.Application.Abstractions.Data;
using TourDock.Domain.Agencies;
using TourDock.Infrastructure.Persistence;

namespace TourDock.Infrastructure.Repositories;

internal sealed class AgencyRepository(TourDockDbContext db) : IAgencyRepository
{
    public Task<Agency?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Agencies.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Agency>> GetForMemberAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Agencies.Where(a => a.Members.Any(m => m.UserId == userId)).OrderBy(a => a.Name).ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<Agency> Items, int TotalCount)> ListAsync(
        AgencyStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Agencies.AsNoTracking().Where(a => status == null || a.Status == status);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(a => a.CreatedAt).ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public void Add(Agency agency) => db.Agencies.Add(agency);
}
