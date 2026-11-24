using FluentValidation;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Domain.Common;

namespace TourDock.Application.Auth;

/// <param name="Code">The authorization code Google sent back to the web app.</param>
/// <param name="CodeVerifier">The PKCE verifier the web app made before sending the visitor to Google.</param>
/// <param name="RedirectUri">The callback the code was issued for; Google checks it matches.</param>
public sealed record SignInWithGoogleCommand(string Code, string CodeVerifier, string RedirectUri) : ICommand<AuthResponse>;

internal sealed class SignInWithGoogleValidator : AbstractValidator<SignInWithGoogleCommand>
{
    public SignInWithGoogleValidator()
    {
        RuleFor(c => c.Code).NotEmpty().MaximumLength(2048);
        RuleFor(c => c.CodeVerifier).NotEmpty().Length(43, 128); // RFC 7636
        RuleFor(c => c.RedirectUri).NotEmpty().MaximumLength(2048)
            .Must(uri => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme is "https" or "http")
            .WithMessage("Redirect URI must be an absolute http(s) URL.");
    }
}

/// <summary>
/// Google has already checked the person (and their email); the account is found, linked or created, and gets the same
/// tokens as a password sign-in. No email confirmation step: Google verified the address.
/// </summary>
internal sealed class SignInWithGoogleHandler(IGoogleSignIn google, IExternalLogins logins, ITokenService tokens)
    : ICommandHandler<SignInWithGoogleCommand, AuthResponse>
{
    public async Task<Result<AuthResponse>> Handle(SignInWithGoogleCommand command, CancellationToken cancellationToken)
    {
        if (google.ClientId is null)
        {
            return ExternalSignInErrors.GoogleUnavailable;
        }

        if (await google.RedeemAsync(command.Code, command.CodeVerifier, command.RedirectUri, cancellationToken) is not { } person)
        {
            return ExternalSignInErrors.Failed;
        }

        var user = await logins.SignInAsync(person, cancellationToken);
        if (user.IsFailure)
        {
            return user.Error;
        }

        return new AuthResponse(user.Value.ToResponse(), await tokens.IssueAsync(user.Value, cancellationToken));
    }
}

/// <summary>Which sign-in options the web app should offer besides email and password.</summary>
/// <param name="GoogleClientId">The OAuth client id for "Continue with Google"; null when it isn't offered.</param>
public sealed record AuthProvidersResponse(string? GoogleClientId);

public sealed record GetAuthProvidersQuery : IQuery<AuthProvidersResponse>;

internal sealed class GetAuthProvidersHandler(IGoogleSignIn google) : IQueryHandler<GetAuthProvidersQuery, AuthProvidersResponse>
{
    public Task<Result<AuthProvidersResponse>> Handle(GetAuthProvidersQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<Result<AuthProvidersResponse>>(new AuthProvidersResponse(google.ClientId));
}
