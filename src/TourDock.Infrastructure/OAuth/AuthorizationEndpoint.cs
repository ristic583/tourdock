using System.Collections.Immutable;
using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using TourDock.Application.Assistant;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace TourDock.Infrastructure.OAuth;

/// <summary>
/// The authorization endpoint (passthrough: OpenIddict has validated the request and parked it; this decides).
/// GET: a returning visitor who already allowed this app gets a code at once; everyone else goes to the web app's
/// consent page, which signs them in first. POST: the consent page's decision, same origin only, by the signed-in
/// visitor, and only for what the page showed them.
/// </summary>
internal static class AuthorizationEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext http, IOpenIddictApplicationManager applications, IOpenIddictAuthorizationManager authorizations,
        IOptions<OAuthOptions> options)
    {
        var request = http.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Not an OpenID request.");
        var application = await applications.FindByClientIdAsync(request.ClientId!)
            ?? throw new InvalidOperationException("Unknown client."); // OpenIddict has already rejected unknown clients
        var applicationId = (await applications.GetIdAsync(application))!;
        var session = await http.AuthenticateAsync(WebSession.Scheme);
        var userId = session.Succeeded ? session.Principal!.FindFirst("sub")?.Value : null;
        var scopes = Granted(request.GetScopes());

        if (HttpMethods.IsPost(http.Request.Method))
        {
            return await DecideAsync(http, request, options.Value, userId, applicationId, scopes, session.Principal, authorizations);
        }

        if (userId is not null && await RememberedAsync(authorizations, userId, applicationId, scopes) is { } remembered)
        {
            return SignIn(session.Principal!, scopes, options.Value.Resource, remembered);
        }

        return Results.Redirect(ConsentUrl(options.Value, request));
    }

    private static async Task<IResult> DecideAsync(
        HttpContext http, OpenIddictRequest request, OAuthOptions options, string? userId, string applicationId,
        ImmutableArray<string> scopes, ClaimsPrincipal? user, IOpenIddictAuthorizationManager authorizations)
    {
        // Lax cookies already keep other sites from posting as the visitor; the origin check is a second lock.
        if (!string.Equals(http.Request.Headers.Origin.ToString(), options.Origin, StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (userId is null)
        {
            return Results.Redirect(ConsentUrl(options, request)); // signed out meanwhile: sign in again on the page
        }

        var form = await http.Request.ReadFormAsync();
        var shownScopes = form["shown_scope"].ToString();
        var shownRedirect = form["shown_redirect_uri"].ToString();
        if (form["decision"] != "allow" || shownScopes != string.Join(' ', request.GetScopes()) || shownRedirect != request.RedirectUri)
        {
            return Deny("The user didn't allow this, or the request changed after the consent page showed it.");
        }

        var authorization = await authorizations.CreateAsync(
            Principal(user!, scopes, options.Resource), userId, applicationId, AuthorizationTypes.Permanent, scopes);
        return SignIn(user!, scopes, options.Resource, (await authorizations.GetIdAsync(authorization))!);
    }

    /// <summary>Only scopes TourDock knows, plus offline_access so the client gets a refresh token.</summary>
    private static ImmutableArray<string> Granted(ImmutableArray<string> requested) =>
        [.. requested.Where(AgentScopes.All.Contains).Append(Scopes.OfflineAccess).Distinct()];

    private static async Task<string?> RememberedAsync(
        IOpenIddictAuthorizationManager authorizations, string userId, string applicationId, ImmutableArray<string> scopes)
    {
        var found = authorizations.FindAsync(userId, applicationId, Statuses.Valid, AuthorizationTypes.Permanent, scopes);
        await foreach (var authorization in found)
        {
            return await authorizations.GetIdAsync(authorization);
        }

        return null;
    }

    private static IResult SignIn(ClaimsPrincipal user, ImmutableArray<string> scopes, string resource, string authorizationId)
    {
        var principal = Principal(user, scopes, resource);
        principal.SetAuthorizationId(authorizationId);
        return Results.SignIn(principal, properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>Who the token is for (sub, name) and what it allows; the access token carries nothing else.</summary>
    private static ClaimsPrincipal Principal(ClaimsPrincipal user, ImmutableArray<string> scopes, string resource)
    {
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.FindFirst("sub")!.Value).SetClaim(Claims.Name, user.FindFirst("name")?.Value);
        identity.SetScopes(scopes).SetResources(resource);
        identity.SetDestinations(_ => [Destinations.AccessToken]);
        return new ClaimsPrincipal(identity);
    }

    private static IResult Deny(string description) => Results.Forbid(
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.AccessDenied,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        }),
        [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

    /// <summary>The consent page gets the parked request's id plus what it should show (checked again on the way back).</summary>
    private static string ConsentUrl(OAuthOptions options, OpenIddictRequest request) =>
        $"{options.ConsentPage}?request_uri={Uri.EscapeDataString(request.RequestUri ?? string.Empty)}"
        + $"&client_id={Uri.EscapeDataString(request.ClientId ?? string.Empty)}"
        + $"&scope={Uri.EscapeDataString(string.Join(' ', request.GetScopes()))}"
        + $"&redirect_uri={Uri.EscapeDataString(request.RedirectUri ?? string.Empty)}";
}
