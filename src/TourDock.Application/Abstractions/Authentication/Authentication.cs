using TourDock.Application.Abstractions.Messaging;
using TourDock.Domain.Common;

namespace TourDock.Application.Abstractions.Authentication;

public sealed record AuthenticatedUser(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles, bool EmailConfirmed = false);

/// <summary>What a sign-up did. The caller answers the same way for all three, so the outcome stays private.</summary>
public enum SignUpOutcome
{
    /// <summary>A new, unconfirmed account.</summary>
    Created,

    /// <summary>The address already has an account that was never confirmed.</summary>
    ExistingUnconfirmed,

    /// <summary>The address already has a confirmed account.</summary>
    ExistingConfirmed,
}

public sealed record SignUp(Guid UserId, SignUpOutcome Outcome);

public sealed record AuthTokens(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

/// <summary>User accounts and credentials (implemented with ASP.NET Core Identity).</summary>
public interface IIdentityService
{
    /// <summary>
    /// Creates an unconfirmed account, or reports that the address already has one (whose password and name stay as
    /// they are). Takes about as long either way, so timing doesn't reveal which addresses are registered.
    /// </summary>
    Task<Result<SignUp>> RegisterAsync(string email, string password, string displayName, CancellationToken cancellationToken);

    /// <summary>Checks credentials, with lockout after repeated failures. Never reveals whether the email exists.</summary>
    Task<Result<AuthenticatedUser>> CheckCredentialsAsync(string email, string password, CancellationToken cancellationToken);

    Task<AuthenticatedUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// A new single-use email confirmation token for the user, replacing any earlier one. Only its hash is stored.
    /// Null when the user doesn't exist or has already confirmed.
    /// </summary>
    Task<string?> CreateEmailConfirmationTokenAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Confirms the email when the token matches and hasn't expired. Confirming twice succeeds.</summary>
    Task<Result<Unit>> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken);

    /// <summary>
    /// Starts an email change after checking the current password: the new address is kept as pending until its link
    /// is opened. When the address already belongs to another account, nothing is pending and its owner's id comes back
    /// (so they can be told), and the caller answers exactly as on success.
    /// </summary>
    Task<Result<EmailChangeRequest>> RequestEmailChangeAsync(Guid userId, string password, string newEmail, CancellationToken cancellationToken);

    /// <summary>The address an email change is waiting to switch to, if any.</summary>
    Task<string?> GetPendingEmailAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>A new single-use token for the pending email change, replacing any earlier one; null when nothing is pending.</summary>
    Task<(string Token, string NewEmail)?> CreateEmailChangeTokenAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Switches to the pending address when the token matches (the address counts as confirmed then).</summary>
    Task<Result<Unit>> ConfirmEmailChangeAsync(Guid userId, string token, CancellationToken cancellationToken);

    /// <summary>Admin support action: mark an account's email as confirmed. Returns the user's id, or not found.</summary>
    Task<Result<Guid>> MarkEmailConfirmedAsync(string email, CancellationToken cancellationToken);
}

/// <param name="ExistingOwnerId">Set when the new address already has an account; nothing was changed then.</param>
public sealed record EmailChangeRequest(Guid? ExistingOwnerId);

/// <summary>Gate for actions that need a confirmed email address (booking, registering an agency).</summary>
public interface IEmailConfirmationGate
{
    /// <summary>Success when the user has confirmed their email, or when confirmation isn't required (configuration).</summary>
    Task<Result<Unit>> EnsureConfirmedAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Issues short-lived access tokens and rotating refresh tokens.</summary>
public interface ITokenService
{
    Task<AuthTokens> IssueAsync(AuthenticatedUser user, CancellationToken cancellationToken);

    /// <summary>
    /// Exchanges a refresh token for a new token pair. The old token is revoked (rotation).
    /// Presenting an already-used token revokes the whole token family (reuse detection).
    /// </summary>
    Task<Result<AuthTokens>> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Revokes the token's whole family (logout on this device).</summary>
    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);
}

public static class AuthErrors
{
    public static readonly Error InvalidCredentials = Error.Validation("Auth.InvalidCredentials", "Email or password is incorrect.");

    /// <summary>
    /// Same answer whether the address has an account or not, and whether this client or the account as a whole is
    /// throttled: it must not reveal who is registered.
    /// </summary>
    public static readonly Error TooManyAttempts =
        Error.TooManyRequests("Auth.TooManyAttempts", "Too many failed sign-in attempts. Please wait a few minutes and try again.");

    /// <summary>The account-wide backstop (many addresses failing on one account) answers exactly like the per-client throttle.</summary>
    public static readonly Error LockedOut = TooManyAttempts;

    public static readonly Error InvalidRefreshToken = Error.Validation("Auth.InvalidRefreshToken", "The refresh token is invalid or expired.");

    public static readonly Error UserNotFound = Error.NotFound("Auth.UserNotFound", "User not found.");

    public static readonly Error EmailNotConfirmed = Error.Forbidden(
        "Auth.EmailNotConfirmed", "Confirm your email address first. We sent you a link when you signed up; you can ask for a new one.");

    public static readonly Error SignInNeedsConfirmedEmail = Error.Forbidden(
        "Auth.SignInNeedsConfirmedEmail", "Confirm your email to sign in. We've sent a new link to your inbox.");

    public static readonly Error InvalidConfirmationLink = Error.Validation(
        "Auth.InvalidConfirmationLink", "This confirmation link is invalid or has expired. Sign in and ask for a new one.");

    public static readonly Error WrongPassword = Error.Validation("Auth.WrongPassword", "Your current password is incorrect.");

    public static readonly Error SameEmail = Error.Validation("Auth.SameEmail", "That's already the email on your account.");

    public static readonly Error InvalidEmailChangeLink = Error.Validation(
        "Auth.InvalidEmailChangeLink", "This link is invalid or has expired. Sign in and change your email again.");

    public static readonly Error EmailNoLongerAvailable = Error.Conflict(
        "Auth.EmailNoLongerAvailable", "This email address now belongs to another account, so it can't be used.");

    public static Error RegistrationFailed(string message) => Error.Validation("Auth.RegistrationFailed", message);
}

/// <summary>
/// Slows down password guessing per account and client address: failures from one address throttle only that address
/// for that email, so a stranger can't lock the owner out. Counts unknown emails too, so throttling reveals nothing.
/// </summary>
public interface ILoginThrottle
{
    bool IsThrottled(string email);

    void RecordFailure(string email);

    void Reset(string email);
}
