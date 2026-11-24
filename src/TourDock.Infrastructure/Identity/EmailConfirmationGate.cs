using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Domain.Common;

namespace TourDock.Infrastructure.Identity;

/// <summary>Settings for user accounts ("Accounts" section).</summary>
public sealed class AccountOptions
{
    public const string SectionName = "Accounts";

    /// <summary>Booking and registering an agency need a confirmed email. On by default; tests may switch it off.</summary>
    public bool RequireConfirmedEmail { get; set; } = true;
}

/// <summary>Reads the confirmation flag from the database, so a link confirmed a second ago counts without a new token.</summary>
internal sealed class EmailConfirmationGate(TourDock.Infrastructure.Persistence.TourDockDbContext db, IOptions<AccountOptions> options) : IEmailConfirmationGate
{
    public async Task<Result<Unit>> EnsureConfirmedAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!options.Value.RequireConfirmedEmail)
        {
            return Unit.Value;
        }

        var confirmed = await db.Users.Where(u => u.Id == userId).Select(u => u.EmailConfirmed).SingleOrDefaultAsync(cancellationToken);
        return confirmed ? Unit.Value : AuthErrors.EmailNotConfirmed;
    }
}
