using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using TourDock.Application.Assistant;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace TourDock.Infrastructure.OAuth;

/// <summary>
/// Dynamic Client Registration (RFC 7591), the way Claude, Claude Code, ChatGPT and the MCP Inspector register
/// themselves today. Public clients only (no secret, PKCE required); redirect URIs must be HTTPS or loopback. A loopback
/// URI is stored without its port, so OpenIddict accepts the random port a native client listens on later.
/// Registration is anonymous, rate-limited, and grants nothing: the user still has to sign in and consent.
/// </summary>
internal static class ClientRegistration
{
    public const string Path = "/connect/register";

    private const int MaxRedirectUris = 5;

    public static async Task<IResult> HandleAsync(
        HttpContext http, IOpenIddictApplicationManager applications, IOptions<OAuthOptions> options, TimeProvider time)
    {
        RegistrationRequest? body;
        try
        {
            body = await http.Request.ReadFromJsonAsync<RegistrationRequest>();
        }
        catch (JsonException)
        {
            body = null;
        }

        if (Validate(body) is { } problem)
        {
            return Results.Json(new { error = "invalid_client_metadata", error_description = problem }, statusCode: 400);
        }

        var descriptor = Descriptor(body!, options.Value);
        await applications.CreateAsync(descriptor);
        return Results.Json(new RegistrationResponse(
            descriptor.ClientId!, time.GetUtcNow().ToUnixTimeSeconds(), descriptor.DisplayName!, body!.RedirectUris!,
            [GrantTypes.AuthorizationCode, GrantTypes.RefreshToken], [ResponseTypes.Code], "none",
            string.Join(' ', AgentScopes.All)), statusCode: 201);
    }

    private static string? Validate(RegistrationRequest? body)
    {
        if (body?.RedirectUris is not { Count: > 0 and <= MaxRedirectUris } uris)
        {
            return $"redirect_uris must list 1 to {MaxRedirectUris} URIs.";
        }

        if (body.TokenEndpointAuthMethod is not (null or "none"))
        {
            return "Only public clients (token_endpoint_auth_method \"none\") can register; use PKCE.";
        }

        return uris.All(IsAllowedRedirect) ? null : "Redirect URIs must be https:// or http://localhost / http://127.0.0.1 (loopback).";
    }

    private static bool IsAllowedRedirect(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && string.IsNullOrEmpty(parsed.Fragment)
        && (parsed.Scheme == Uri.UriSchemeHttps || (parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback));

    private static OpenIddictApplicationDescriptor Descriptor(RegistrationRequest body, OAuthOptions options)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = Guid.CreateVersion7().ToString("N"),
            ClientType = ClientTypes.Public,
            ApplicationType = ApplicationTypes.Native,
            ConsentType = ConsentTypes.Explicit,
            DisplayName = Name(body.ClientName),
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };
        foreach (var uri in body.RedirectUris!.Select(u => new Uri(u)))
        {
            descriptor.RedirectUris.Add(uri.IsLoopback ? new UriBuilder(uri) { Port = -1 }.Uri : uri);
        }

        descriptor.Permissions.UnionWith(
        [
            Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.GrantTypes.AuthorizationCode,
            Permissions.GrantTypes.RefreshToken, Permissions.ResponseTypes.Code, Permissions.Prefixes.Resource + options.Resource,
            .. AgentScopes.All.Append(Scopes.OfflineAccess).Select(scope => Permissions.Prefixes.Scope + scope),
        ]);
        return descriptor;
    }

    /// <summary>Shown on the consent page: printable characters only, and short.</summary>
    private static string Name(string? name)
    {
        var clean = new string((name ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length == 0 ? "An unnamed app" : clean[..Math.Min(clean.Length, 80)];
    }

    internal sealed record RegistrationRequest(
        [property: JsonPropertyName("client_name")] string? ClientName,
        [property: JsonPropertyName("redirect_uris")] List<string>? RedirectUris,
        [property: JsonPropertyName("token_endpoint_auth_method")] string? TokenEndpointAuthMethod);

    internal sealed record RegistrationResponse(
        [property: JsonPropertyName("client_id")] string ClientId,
        [property: JsonPropertyName("client_id_issued_at")] long ClientIdIssuedAt,
        [property: JsonPropertyName("client_name")] string ClientName,
        [property: JsonPropertyName("redirect_uris")] IReadOnlyList<string> RedirectUris,
        [property: JsonPropertyName("grant_types")] IReadOnlyList<string> GrantTypes,
        [property: JsonPropertyName("response_types")] IReadOnlyList<string> ResponseTypes,
        [property: JsonPropertyName("token_endpoint_auth_method")] string TokenEndpointAuthMethod,
        [property: JsonPropertyName("scope")] string Scope);
}
