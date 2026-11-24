using System.ComponentModel.DataAnnotations;

namespace TourDock.Infrastructure.OAuth;

/// <summary>
/// The OAuth 2.1 authorization server that lets MCP clients act for a signed-in user (ADR 0028). Everything is served
/// from the public origin of the web app: the edge routes /connect, /.well-known and /mcp to the API.
/// </summary>
public sealed class OAuthOptions
{
    public const string SectionName = "OAuth";

    /// <summary>The public origin (no trailing slash), e.g. https://tourdock.example.com. Also the token issuer.</summary>
    [Required]
    [Url]
    public string Issuer { get; init; } = "http://localhost:3000";

    /// <summary>
    /// RSA private key (PEM; line breaks may be written as \n) that signs tokens. Empty: a new key on every start, so
    /// connected apps sign in again after a restart.
    /// </summary>
    public string? SigningKeyPem { get; init; }

    /// <summary>256-bit key (base64) that encrypts tokens. Empty: a new key on every start.</summary>
    public string? EncryptionKey { get; init; }

    public string Origin => Issuer.TrimEnd('/');

    /// <summary>The MCP endpoint tokens are issued for (RFC 8707 resource, token audience).</summary>
    public string Resource => $"{Origin}{McpPaths.Account}";

    /// <summary>The web app's consent page; it makes the visitor sign in first.</summary>
    public string ConsentPage => $"{Origin}/oauth/consent";
}

/// <summary>Where the MCP endpoints live: public tools anonymously, account tools with an access token.</summary>
public static class McpPaths
{
    public const string Public = "/mcp";
    public const string Account = "/mcp/account";
}
