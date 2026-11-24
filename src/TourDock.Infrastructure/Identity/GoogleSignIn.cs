using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using TourDock.Application.Abstractions.Authentication;

namespace TourDock.Infrastructure.Identity;

/// <summary>A Google OAuth client (web application). Without both values, "Continue with Google" isn't offered.</summary>
public sealed class GoogleOptions
{
    public const string SectionName = "Google";

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    /// <summary>Where Google publishes its OpenID configuration and signing keys.</summary>
    [Required]
    [Url]
    public string Authority { get; init; } = "https://accounts.google.com";

    [Required]
    [Url]
    public string TokenEndpoint { get; init; } = "https://oauth2.googleapis.com/token";

    public bool IsEnabled => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>
/// Redeems Google's authorization code (client secret + PKCE verifier) and checks the ID token that comes back: Google's
/// signature (keys from its published configuration, cached), issuer, this client as audience, and expiry.
/// </summary>
internal sealed partial class GoogleSignIn : IGoogleSignIn
{
    public const string HttpClientName = "google";

    private readonly IHttpClientFactory _http;
    private readonly GoogleOptions _options;
    private readonly ILogger<GoogleSignIn> _logger;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configuration;

    public GoogleSignIn(IHttpClientFactory http, IOptions<GoogleOptions> options, ILogger<GoogleSignIn> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _configuration = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{_options.Authority.TrimEnd('/')}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(http.CreateClient(HttpClientName)));
    }

    public string? ClientId => _options.IsEnabled ? _options.ClientId : null;

    public async Task<ExternalIdentity?> RedeemAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken)
    {
        if (!_options.IsEnabled)
        {
            return null;
        }

        using var response = await _http.CreateClient(HttpClientName).PostAsync(_options.TokenEndpoint, new FormUrlEncodedContent(
        [
            new("grant_type", "authorization_code"), new("code", code), new("code_verifier", codeVerifier),
            new("redirect_uri", redirectUri), new("client_id", _options.ClientId!), new("client_secret", _options.ClientSecret!),
        ]), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            LogRefused((int)response.StatusCode); // e.g. an expired or reused code; the body may echo the request, so it isn't logged
            return null;
        }

        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        return tokens?.IdToken is { } idToken ? await ValidateAsync(idToken, cancellationToken) : null;
    }

    private async Task<ExternalIdentity?> ValidateAsync(string idToken, CancellationToken cancellationToken)
    {
        var configuration = await _configuration.GetConfigurationAsync(cancellationToken);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            // Google writes its issuer with and without the scheme.
            ValidIssuers = [configuration.Issuer, new Uri(configuration.Issuer).Host],
            ValidAudience = _options.ClientId,
            IssuerSigningKeys = configuration.SigningKeys,
            ClockSkew = TimeSpan.FromMinutes(2),
        });
        if (!result.IsValid || result.SecurityToken is not JsonWebToken token)
        {
            LogInvalidToken(result.Exception?.GetType().Name ?? "unknown");
            return null;
        }

        var email = token.TryGetPayloadValue<string>("email", out var e) ? e : null;
        var subject = token.Subject;
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(subject))
        {
            return null;
        }

        var verified = token.TryGetPayloadValue<bool>("email_verified", out var flag) && flag;
        var name = token.TryGetPayloadValue<string>("name", out var n) ? n : null;
        return new ExternalIdentity("Google", subject, email, verified, name);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Google refused the authorization code ({Status})")]
    private partial void LogRefused(int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Google ID token rejected ({Reason})")]
    private partial void LogInvalidToken(string reason);

    private sealed record TokenResponse([property: JsonPropertyName("id_token")] string? IdToken);
}

public static class GoogleRegistration
{
    public static IServiceCollection AddGoogleSignIn(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GoogleOptions>()
            .Bind(configuration.GetSection(GoogleOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHttpClient(GoogleSignIn.HttpClientName).AddStandardResilienceHandler(o =>
        {
            o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(12);
            o.Retry.MaxRetryAttempts = 1;
            o.Retry.ShouldHandle = args => ValueTask.FromResult(args.Outcome.Exception is HttpRequestException); // a code is single-use
        });
        services.AddSingleton<IGoogleSignIn, GoogleSignIn>();
        services.AddScoped<IExternalLogins, ExternalLogins>();
        return services;
    }
}
