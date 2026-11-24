using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using TourDock.Application.Assistant;
using TourDock.Infrastructure.Identity;
using TourDock.Infrastructure.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace TourDock.Infrastructure.OAuth;

public static class OAuthRegistration
{
    /// <summary>
    /// OpenIddict as an OAuth 2.1 authorization server (authorization code + PKCE S256, rotating refresh tokens, resource
    /// indicators), its token validation for the MCP account endpoint, and a scheme that recognises the web app's
    /// signed-in visitor from the session cookie, so the consent step needs no second sign-in.
    /// </summary>
    public static IServiceCollection AddOAuthServer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OAuthOptions>()
            .Bind(configuration.GetSection(OAuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        var options = configuration.GetSection(OAuthOptions.SectionName).Get<OAuthOptions>() ?? new OAuthOptions();

        services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore().UseDbContext<TourDockDbContext>().ReplaceDefaultEntities<Guid>())
            .AddServer(server => ConfigureServer(server, options))
            .AddValidation(validation =>
            {
                validation.UseLocalServer();
                validation.UseAspNetCore();
                validation.AddAudiences(options.Resource); // a token for anything else is refused
            });

        services.AddAuthentication().AddJwtBearer(WebSession.Scheme, _ => { });
        services.AddOptions<JwtBearerOptions>(WebSession.Scheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) => WebSession.Configure(bearer, jwt.Value));
        return services;
    }

    private static void ConfigureServer(OpenIddictServerBuilder server, OAuthOptions options)
    {
        server.SetIssuer(new Uri(options.Origin + "/"));
        server.SetAuthorizationEndpointUris("connect/authorize").SetTokenEndpointUris("connect/token");
        server.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow().RequireProofKeyForCodeExchange();
        server.Configure(o => o.CodeChallengeMethods.Remove(CodeChallengeMethods.Plain)); // S256 only
        server.RegisterScopes([.. AgentScopes.All, Scopes.OfflineAccess]);
        server.RegisterResources(options.Resource);
        server.SetAccessTokenLifetime(TimeSpan.FromMinutes(15)).SetRefreshTokenLifetime(TimeSpan.FromDays(14));

        // The consent page is in the web app: the request is parked server-side and the page posts back only its id.
        server.EnableAuthorizationRequestCaching();
        AddKeys(server, options);

        // TLS ends at the proxy in front of the edge; the API only ever sees plain HTTP from the edge.
        server.UseAspNetCore().EnableAuthorizationEndpointPassthrough().DisableTransportSecurityRequirement();
        server.AddEventHandler<OpenIddictServerEvents.HandleConfigurationRequestContext>(handler => handler.UseInlineHandler(context =>
        {
            context.Metadata["registration_endpoint"] = $"{options.Origin}{ClientRegistration.Path}";
            return default;
        }));
    }

    private static void AddKeys(OpenIddictServerBuilder server, OAuthOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SigningKeyPem))
        {
            server.AddEphemeralSigningKey();
        }
        else
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(options.SigningKeyPem.Replace("\\n", "\n", StringComparison.Ordinal)); // one line in .env
            server.AddSigningKey(new RsaSecurityKey(rsa));
        }

        if (string.IsNullOrWhiteSpace(options.EncryptionKey))
        {
            server.AddEphemeralEncryptionKey();
        }
        else
        {
            server.AddEncryptionKey(new SymmetricSecurityKey(Convert.FromBase64String(options.EncryptionKey)));
        }
    }
}
