using Microsoft.EntityFrameworkCore;
using TourDock.Application.Abstractions;
using TourDock.Infrastructure.Persistence;

namespace TourDock.Infrastructure.Identity;

internal sealed class UserDirectory(TourDockDbContext db) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, UserContact>> GetContactsAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new UserContact(u.DisplayName, u.Email ?? string.Empty), cancellationToken);
    }
}
