using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Notifications;
using TourDock.Domain.Common;

namespace TourDock.Infrastructure.Identity;

internal sealed class IdentityService(UserManager<ApplicationUser> users, TimeProvider timeProvider) : IIdentityService
{
    // Stored in Identity's user tokens table as "<expires unix seconds>.<base64url SHA-256 of the token>".
    private const string TokenProvider = "TourDock";
    private const string ConfirmationTokenName = "EmailConfirmation";
    private const string EmailChangeTokenName = "EmailChange";
    private const string PendingEmailTokenName = "EmailChangePending";

    public async Task<Result<SignUp>> RegisterAsync(
        string email, string password, string displayName, CancellationToken cancellationToken)
    {
        if (await users.FindByEmailAsync(email) is { } existing)
        {
            // Hash the password anyway, so an existing address takes about as long as a new account.
            _ = users.PasswordHasher.HashPassword(existing, password);
            return Existing(existing);
        }

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            DisplayName = displayName.Trim(),
            CreatedAt = timeProvider.GetUtcNow(),
        };

        var created = await users.CreateAsync(user, password);
        if (created.Succeeded)
        {
            return new SignUp(user.Id, SignUpOutcome.Created);
        }

        // Two sign-ups for the same address at once: the other one won.
        if (created.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName") && await users.FindByEmailAsync(email) is { } winner)
        {
            return Existing(winner);
        }

        return AuthErrors.RegistrationFailed(string.Join(" ", created.Errors.Select(e => e.Description)));
    }

    private static SignUp Existing(ApplicationUser user) =>
        new(user.Id, user.EmailConfirmed ? SignUpOutcome.ExistingConfirmed : SignUpOutcome.ExistingUnconfirmed);

    public async Task<Result<AuthenticatedUser>> CheckCredentialsAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            // Same answer as a wrong password: don't reveal which emails are registered.
            return AuthErrors.InvalidCredentials;
        }

        if (await users.IsLockedOutAsync(user))
        {
            return AuthErrors.LockedOut;
        }

        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);
            return await users.IsLockedOutAsync(user) ? AuthErrors.LockedOut : AuthErrors.InvalidCredentials;
        }

        await users.ResetAccessFailedCountAsync(user);
        return await ToAuthenticatedUserAsync(user);
    }

    public async Task<AuthenticatedUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return user is null ? null : await ToAuthenticatedUserAsync(user);
    }

    public async Task<string?> CreateEmailConfirmationTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || user.EmailConfirmed)
        {
            return null;
        }

        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var expires = timeProvider.GetUtcNow().Add(AccountEmails.ConfirmationLinkLifetime).ToUnixTimeSeconds();
        await users.SetAuthenticationTokenAsync(user, TokenProvider, ConfirmationTokenName, $"{expires}.{Hash(token)}");
        return token;
    }

    public async Task<Result<Unit>> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken)
    {
        var user = await users.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return AuthErrors.InvalidConfirmationLink;
        }

        if (user.EmailConfirmed)
        {
            return Unit.Value; // the link was opened twice
        }

        var stored = await users.GetAuthenticationTokenAsync(user, TokenProvider, ConfirmationTokenName);
        var parts = stored?.Split('.', 2);
        if (parts is not [var expires, var hash]
            || !long.TryParse(expires, out var expiresAt)
            || timeProvider.GetUtcNow().ToUnixTimeSeconds() > expiresAt
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(Hash(token))))
        {
            return AuthErrors.InvalidConfirmationLink;
        }

        user.EmailConfirmed = true;
        await users.UpdateAsync(user);
        await users.RemoveAuthenticationTokenAsync(user, TokenProvider, ConfirmationTokenName);
        return Unit.Value;
    }

    public async Task<Result<EmailChangeRequest>> RequestEmailChangeAsync(Guid userId, string password, string newEmail, CancellationToken cancellationToken)
    {
        var user = await users.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return AuthErrors.UserNotFound;
        }

        if (await users.IsLockedOutAsync(user))
        {
            return AuthErrors.LockedOut;
        }

        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user); // counts toward lockout, like a failed sign-in
            return await users.IsLockedOutAsync(user) ? AuthErrors.LockedOut : AuthErrors.WrongPassword;
        }

        await users.ResetAccessFailedCountAsync(user);
        if (string.Equals(user.Email, newEmail, StringComparison.OrdinalIgnoreCase))
        {
            return AuthErrors.SameEmail;
        }

        if (await users.FindByEmailAsync(newEmail) is { } owner)
        {
            return new EmailChangeRequest(owner.Id);
        }

        await users.SetAuthenticationTokenAsync(user, TokenProvider, PendingEmailTokenName, newEmail.Trim());
        await users.RemoveAuthenticationTokenAsync(user, TokenProvider, EmailChangeTokenName); // an older link stops working
        return new EmailChangeRequest(null);
    }

    public async Task<string?> GetPendingEmailAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return user is null ? null : await users.GetAuthenticationTokenAsync(user, TokenProvider, PendingEmailTokenName);
    }

    public async Task<(string Token, string NewEmail)?> CreateEmailChangeTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        var pending = user is null ? null : await users.GetAuthenticationTokenAsync(user, TokenProvider, PendingEmailTokenName);
        if (user is null || string.IsNullOrEmpty(pending))
        {
            return null;
        }

        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var expires = timeProvider.GetUtcNow().Add(AccountEmails.ConfirmationLinkLifetime).ToUnixTimeSeconds();
        await users.SetAuthenticationTokenAsync(user, TokenProvider, EmailChangeTokenName, $"{expires}.{Hash(token)}");
        return (token, pending);
    }

    public async Task<Result<Unit>> ConfirmEmailChangeAsync(Guid userId, string token, CancellationToken cancellationToken)
    {
        var user = await users.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return AuthErrors.InvalidEmailChangeLink;
        }

        var pending = await users.GetAuthenticationTokenAsync(user, TokenProvider, PendingEmailTokenName);
        if (string.IsNullOrEmpty(pending) || !Matches(await users.GetAuthenticationTokenAsync(user, TokenProvider, EmailChangeTokenName), token))
        {
            return AuthErrors.InvalidEmailChangeLink;
        }

        // Someone may have signed up with the address since the change was requested.
        if (await users.FindByEmailAsync(pending) is { } other && other.Id != user.Id)
        {
            await ClearEmailChangeAsync(user);
            return AuthErrors.EmailNoLongerAvailable;
        }

        user.Email = pending;
        user.UserName = pending;
        user.EmailConfirmed = true; // opening the link proved the new address
        var updated = await users.UpdateAsync(user); // also normalizes and rotates the security stamp
        if (!updated.Succeeded)
        {
            return AuthErrors.EmailNoLongerAvailable;
        }

        await ClearEmailChangeAsync(user);
        return Unit.Value;
    }

    public async Task<Result<Guid>> MarkEmailConfirmedAsync(string email, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            return AuthErrors.UserNotFound;
        }

        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await users.UpdateAsync(user);
            await users.RemoveAuthenticationTokenAsync(user, TokenProvider, ConfirmationTokenName);
        }

        return user.Id;
    }

    private async Task ClearEmailChangeAsync(ApplicationUser user)
    {
        await users.RemoveAuthenticationTokenAsync(user, TokenProvider, EmailChangeTokenName);
        await users.RemoveAuthenticationTokenAsync(user, TokenProvider, PendingEmailTokenName);
    }

    /// <summary>Checks a link token against the stored "expires.hash", in constant time.</summary>
    private bool Matches(string? stored, string token)
    {
        var parts = stored?.Split('.', 2);
        return parts is [var expires, var hash]
            && long.TryParse(expires, out var expiresAt)
            && timeProvider.GetUtcNow().ToUnixTimeSeconds() <= expiresAt
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(Hash(token)));
    }

    private static string Hash(string token) => Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private async Task<AuthenticatedUser> ToAuthenticatedUserAsync(ApplicationUser user) =>
        new(user.Id, user.Email!, user.DisplayName, [.. await users.GetRolesAsync(user)], user.EmailConfirmed);
}
