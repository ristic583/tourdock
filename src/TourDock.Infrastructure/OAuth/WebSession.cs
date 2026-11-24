using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using TourDock.Infrastructure.Identity;

namespace TourDock.Infrastructure.OAuth;

/// <summary>
/// Recognises the web app's signed-in visitor on the OAuth authorization endpoint. The edge serves the web app and
/// /connect from one origin, so the browser sends the session cookie the web app set (httpOnly, SameSite=Lax): the
/// API access token, base64url-encoded. It is validated exactly like a Bearer token. Used only by the authorization
/// endpoint; everywhere else the API reads tokens from the Authorization header.
/// </summary>
internal static class WebSession
{
    public const string Scheme = "WebSession";

    /// <summary>The web app's access-token cookie (web/src/lib/auth/cookies.ts).</summary>
    public const string Cookie = "td_at";

    public static void Configure(JwtBearerOptions bearer, JwtOptions jwt)
    {
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = TokenService.SigningKey(jwt),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            NameClaimType = "name",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        bearer.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.Token = Decode(context.Request.Cookies[Cookie]);
                return Task.CompletedTask;
            },
        };
    }

    /// <summary>The web app stores the token base64url-encoded (cookie-safe alphabet, no padding).</summary>
    internal static string? Decode(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=')));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
