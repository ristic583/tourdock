using TourDock.Domain.Common;

namespace TourDock.Application.Abstractions.Authentication;

/// <summary>Who an identity provider says signed in: its stable id for the person, and the email it vouches for.</summary>
/// <param name="Subject">The provider's id for the person; it never changes, unlike the email.</param>
/// <param name="EmailVerified">True when the provider has checked that the person owns <paramref name="Email"/>.</param>
public sealed record ExternalIdentity(string Provider, string Subject, string Email, bool EmailVerified, string? Name);

/// <summary>
/// "Continue with Google" (ADR 0029). The browser brings back an authorization code; the code is redeemed here, with
/// the client secret and the PKCE verifier, and Google's ID token is checked before anyone is signed in.
/// </summary>
public interface IGoogleSignIn
{
    /// <summary>The OAuth client id the browser sends to Google; null when Google sign-in isn't configured.</summary>
    string? ClientId { get; }

    /// <summary>Redeems the code and checks the ID token. Null when Google refuses the code or the token doesn't hold up.</summary>
    Task<ExternalIdentity?> RedeemAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken);
}

/// <summary>Accounts for people an identity provider vouches for (implemented with ASP.NET Core Identity logins).</summary>
public interface IExternalLogins
{
    /// <summary>
    /// The account already linked to this provider id; else the account with the same email, linked now (only when the
    /// provider verified the email); else a new account with that email, confirmed and without a password.
    /// </summary>
    Task<Result<AuthenticatedUser>> SignInAsync(ExternalIdentity identity, CancellationToken cancellationToken);
}

public static class ExternalSignInErrors
{
    public static readonly Error GoogleUnavailable = Error.Unavailable(
        "Auth.GoogleUnavailable", "Signing in with Google isn't available right now. Use your email and password.");

    public static readonly Error Failed = Error.Validation(
        "Auth.ExternalSignInFailed", "Google didn't confirm the sign-in. Please try again.");

    public static readonly Error EmailNotVerified = Error.Validation(
        "Auth.ExternalEmailNotVerified", "Your Google account's email address isn't verified, so it can't be used to sign in here.");
}
