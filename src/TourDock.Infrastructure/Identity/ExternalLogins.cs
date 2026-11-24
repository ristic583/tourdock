using Microsoft.AspNetCore.Identity;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Domain.Common;

namespace TourDock.Infrastructure.Identity;

/// <summary>
/// Accounts behind "Continue with Google" (ADR 0029). A provider id stays linked to one account in <c>user_logins</c>.
/// The first sign-in links an account with the same, provider-verified email; without one, a new account is created.
/// </summary>
internal sealed class ExternalLogins(UserManager<ApplicationUser> users, TimeProvider time) : IExternalLogins
{
    private const int MaxDisplayName = 80;

    public async Task<Result<AuthenticatedUser>> SignInAsync(ExternalIdentity identity, CancellationToken cancellationToken)
    {
        var user = await users.FindByLoginAsync(identity.Provider, identity.Subject);
        if (user is null)
        {
            if (!identity.EmailVerified)
            {
                return ExternalSignInErrors.EmailNotVerified;
            }

            var linked = await LinkOrCreateAsync(identity);
            if (linked.IsFailure)
            {
                return linked.Error;
            }

            user = linked.Value;
        }

        if (await users.IsLockedOutAsync(user))
        {
            return AuthErrors.LockedOut;
        }

        return new AuthenticatedUser(user.Id, user.Email!, user.DisplayName, [.. await users.GetRolesAsync(user)], user.EmailConfirmed);
    }

    private async Task<Result<ApplicationUser>> LinkOrCreateAsync(ExternalIdentity identity)
    {
        var user = await users.FindByEmailAsync(identity.Email) ?? await CreateAsync(identity);
        if (user is null)
        {
            return AuthErrors.RegistrationFailed("The account couldn't be created. Please try again.");
        }

        if (!user.EmailConfirmed)
        {
            // Nobody proved they own this address when this account was made, so whoever chose its password may not be
            // the person Google vouches for (account pre-hijacking). The password goes; the address counts as confirmed.
            await users.RemovePasswordAsync(user);
            user.EmailConfirmed = true;
            await users.UpdateAsync(user);
        }

        var added = await users.AddLoginAsync(user, new UserLoginInfo(identity.Provider, identity.Subject, identity.Provider));
        if (!added.Succeeded && await users.FindByLoginAsync(identity.Provider, identity.Subject) is null)
        {
            return AuthErrors.RegistrationFailed(string.Join(" ", added.Errors.Select(e => e.Description)));
        }

        return user;
    }

    /// <summary>A confirmed account without a password. If two first sign-ins race, the one that lost reads the winner's.</summary>
    private async Task<ApplicationUser?> CreateAsync(ExternalIdentity identity)
    {
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = identity.Email,
            Email = identity.Email,
            EmailConfirmed = true,
            DisplayName = DisplayName(identity),
            CreatedAt = time.GetUtcNow(),
        };

        return (await users.CreateAsync(user)).Succeeded ? user : await users.FindByEmailAsync(identity.Email);
    }

    private static string DisplayName(ExternalIdentity identity)
    {
        var name = string.IsNullOrWhiteSpace(identity.Name) ? identity.Email.Split('@')[0] : identity.Name.Trim();
        return name[..Math.Min(name.Length, MaxDisplayName)];
    }
}
