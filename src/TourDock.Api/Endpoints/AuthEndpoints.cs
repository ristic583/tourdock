using TourDock.Api.Infrastructure;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Auth;
using TourDock.Infrastructure;

namespace TourDock.Api.Endpoints;

internal static class AuthEndpoints
{
    public const string RateLimitPolicy = Hardening.AuthPolicy;

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        MapSignIn(api);
        MapAccount(api);
        return api;
    }

    private static void MapSignIn(RouteGroupBuilder api)
    {
        // Each endpoint declares its own access: AllowAnonymous on the group would also open the signed-in ones below.
        var auth = api.MapGroup("/auth").WithTags("Auth").RequireRateLimiting(RateLimitPolicy);

        auth.MapPost("/register", async (RegisterCommand command, ICommandHandler<RegisterCommand, Unit> handler, CancellationToken ct) =>
                (await handler.Handle(command, ct)).ToHttp(_ => TypedResults.Accepted((string?)null)))
            .AllowAnonymous()
            .WithSummary("Sign up: always 202; the email gets a confirmation link (or a notice if it already has an account)");

        auth.MapPost("/login", async (LoginCommand command, ICommandHandler<LoginCommand, AuthResponse> handler, CancellationToken ct) =>
                (await handler.Handle(command, ct)).ToHttp())
            .AllowAnonymous()
            .WithSummary("Sign in with email and password");

        auth.MapPost("/google", async (SignInWithGoogleCommand command, ICommandHandler<SignInWithGoogleCommand, AuthResponse> handler,
            CancellationToken ct) =>
                (await handler.Handle(command, ct)).ToHttp())
            .AllowAnonymous()
            .WithSummary("Sign in with Google: redeem the authorization code the web app received (ADR 0029)");

        // Read on every sign-in page view, so it stays out of the sign-in rate limit (only the global limit applies).
        api.MapGet("/auth/providers", async (IQueryHandler<GetAuthProvidersQuery, AuthProvidersResponse> handler, CancellationToken ct) =>
                (await handler.Handle(new GetAuthProvidersQuery(), ct)).ToHttp())
            .AllowAnonymous()
            .WithTags("Auth")
            .Produces<AuthProvidersResponse>()
            .WithSummary("Sign-in options besides email and password (Google's client id when it's offered)");

        auth.MapPost("/refresh", async (RefreshCommand command, ICommandHandler<RefreshCommand, AuthTokens> handler,
            CancellationToken ct) =>
                (await handler.Handle(command, ct)).ToHttp())
            .AllowAnonymous()
            .WithSummary("Exchange a refresh token for a new token pair (rotation)");

        auth.MapPost("/logout", async (LogoutCommand command, ICommandHandler<LogoutCommand, Unit> handler, CancellationToken ct) =>
                (await handler.Handle(command, ct)).ToHttp(_ => TypedResults.NoContent()))
            .AllowAnonymous()
            .WithSummary("Revoke the refresh token (and its whole session)");

        auth.MapPost("/verify-email", async (ConfirmEmailCommand command, ICommandHandler<ConfirmEmailCommand, Unit> handler,
            CancellationToken ct) =>
                (await handler.Handle(command, ct)).ToHttp(_ => TypedResults.NoContent()))
            .AllowAnonymous()
            .WithSummary("Confirm the email address with the link's user id and token");

        auth.MapPost("/verify-email/resend", async (ICommandHandler<ResendEmailConfirmationCommand, Unit> handler, CancellationToken ct) =>
                (await handler.Handle(new ResendEmailConfirmationCommand(), ct)).ToHttp(_ => TypedResults.NoContent()))
            .RequireAuthorization()
            .RequireRateLimiting(Hardening.AccountEmailPolicy) // per account, a few an hour
            .WithSummary("Email the signed-in user a new confirmation link");

        auth.MapPost("/verify-email-change", async (ConfirmEmailChangeCommand command,
            ICommandHandler<ConfirmEmailChangeCommand, Unit> handler, CancellationToken ct) =>
                (await handler.Handle(command, ct)).ToHttp(_ => TypedResults.NoContent()))
            .AllowAnonymous()
            .WithSummary("Switch to the new email address with the link's user id and token");
    }

    /// <summary>The signed-in user's own account, and support actions on accounts.</summary>
    private static void MapAccount(RouteGroupBuilder api)
    {
        api.MapPost("/me/email", async (RequestEmailChangeCommand command, ICommandHandler<RequestEmailChangeCommand, Unit> handler,
            CancellationToken ct) =>
                (await handler.Handle(command, ct)).ToHttp(_ => TypedResults.Accepted("/api/v1/me")))
            .RequireAuthorization()
            .RequireRateLimiting(Hardening.AccountEmailPolicy)
            .WithTags("Auth")
            .WithSummary("Change the email: needs the current password; the new address gets a link and changes only once it's opened");

        api.MapPost("/admin/users/confirm-email", async (AdminConfirmEmailCommand command,
            ICommandHandler<AdminConfirmEmailCommand, Unit> handler, CancellationToken ct) =>
                (await handler.Handle(command, ct)).ToHttp(_ => TypedResults.NoContent()))
            .RequireAuthorization(AuthPolicies.Admin)
            .WithTags("Admin")
            .WithSummary("Mark an account's email as confirmed (support; recorded in the audit log)");

        api.MapGet("/me", async (IQueryHandler<GetCurrentUserQuery, UserResponse> handler, CancellationToken ct) =>
                (await handler.Handle(new GetCurrentUserQuery(), ct)).ToHttp())
            .RequireAuthorization()
            .WithTags("Auth")
            .WithSummary("The signed-in user");
    }
}
