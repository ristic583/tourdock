using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Abstractions;

namespace TourDock.Infrastructure.OAuth;

public static class OAuthEndpoints
{
    public const string RegistrationRateLimitPolicy = "oauth-register";

    /// <summary>
    /// The endpoints OpenIddict leaves to the app: authorization (passthrough), client registration, and what the web
    /// app's consent page needs to know about a client. Token and metadata endpoints are OpenIddict's own.
    /// </summary>
    public static IEndpointRouteBuilder MapOAuth(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], AuthorizationEndpoint.HandleAsync)
            .AllowAnonymous() // the visitor is recognised from the web session inside the handler
            .ExcludeFromDescription();

        app.MapPost(ClientRegistration.Path, ClientRegistration.HandleAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RegistrationRateLimitPolicy)
            .ExcludeFromDescription();

        app.MapGet("/api/v1/oauth/clients/{clientId}", async (string clientId, IOpenIddictApplicationManager applications) =>
                await applications.FindByClientIdAsync(clientId) is { } application
                    ? Results.Ok(new OAuthClientResponse(
                        clientId,
                        await applications.GetDisplayNameAsync(application) ?? clientId,
                        [.. await applications.GetRedirectUrisAsync(application)]))
                    : Results.NotFound())
            .RequireAuthorization()
            .WithTags("OAuth")
            .Produces<OAuthClientResponse>()
            .WithSummary("An app asking for access to the signed-in user's account, for the consent page");

        return app;
    }
}

/// <summary>What the consent page shows about an app: its name and where it sends the user back to.</summary>
public sealed record OAuthClientResponse(string ClientId, string Name, IReadOnlyList<string> RedirectUris);
