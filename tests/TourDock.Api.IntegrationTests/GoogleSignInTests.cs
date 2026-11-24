using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TourDock.Infrastructure.Identity;

namespace TourDock.Api.IntegrationTests;

/// <summary>Google sign-in against a fake Google (its configuration, keys and token endpoint), with confirmed emails required.</summary>
public sealed class GoogleApiFactory : TourDockApiFactory
{
    public const string ClientId = "test-client.apps.googleusercontent.com";

    public FakeGoogle Google { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Accounts:RequireConfirmedEmail", "true");
        builder.UseSetting("Google:ClientId", ClientId);
        builder.UseSetting("Google:ClientSecret", "test-secret");
        builder.UseSetting("Google:Authority", FakeGoogle.Issuer);
        builder.UseSetting("Google:TokenEndpoint", FakeGoogle.TokenEndpoint);
        builder.ConfigureTestServices(services =>
            services.AddHttpClient(GoogleSignIn.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Google));
    }
}

public sealed class GoogleSignInTests(GoogleApiFactory factory) : IClassFixture<GoogleApiFactory>
{
    private const string Redirect = "https://tourdock.test/api/auth/google/callback";
    private const string Verifier = "verifier-0123456789-0123456789-0123456789-0123";

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private TestApi Api() => new(factory.CreateClient(), Ct);

    [Fact]
    public async Task A_first_google_sign_in_creates_a_confirmed_account_and_the_next_one_finds_it()
    {
        var email = $"new-{Guid.NewGuid():N}@gmail.test";
        var subject = Guid.NewGuid().ToString("N");

        var (status, first) = await SignInAsync(factory.Google.Issue(subject, email, name: "Ana Traveler"));
        var (_, again) = await SignInAsync(factory.Google.Issue(subject, email));

        status.Should().Be(HttpStatusCode.OK);
        first.GetProperty("user").GetProperty("emailConfirmed").GetBoolean().Should().BeTrue("Google verified the address");
        first.GetProperty("user").GetProperty("displayName").GetString().Should().Be("Ana Traveler");
        first.GetProperty("tokens").GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        again.GetProperty("user").GetProperty("id").GetString().Should().Be(first.GetProperty("user").GetProperty("id").GetString());
        factory.Google.LastTokenRequest.Should().Contain("code_verifier=" + Verifier).And.Contain("client_secret=test-secret");
    }

    [Fact]
    public async Task A_confirmed_account_with_the_same_email_is_linked_and_keeps_its_password()
    {
        var api = Api();
        var email = await api.SignUpAsync();
        (await api.SendAsync(HttpMethod.Post, "/api/v1/admin/users/confirm-email", new { email }, await api.AdminTokenAsync())).Status
            .Should().Be(HttpStatusCode.NoContent);
        var passwordUser = await api.SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { email, password = TestApi.Password });

        var (status, google) = await SignInAsync(factory.Google.Issue(Guid.NewGuid().ToString("N"), email.ToUpperInvariant()));

        status.Should().Be(HttpStatusCode.OK);
        google.GetProperty("user").GetProperty("id").GetString().Should().Be(passwordUser.Body.GetProperty("user").GetProperty("id").GetString());
        await api.LoginAsync(email); // the password still works
    }

    [Fact]
    public async Task Linking_an_unconfirmed_account_removes_the_password_someone_else_may_have_chosen()
    {
        var api = Api();
        var email = await api.SignUpAsync(); // never confirmed: anyone could have typed this address

        var (status, google) = await SignInAsync(factory.Google.Issue(Guid.NewGuid().ToString("N"), email));
        var (passwordStatus, refused) = await api.SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { email, password = TestApi.Password });

        status.Should().Be(HttpStatusCode.OK);
        google.GetProperty("user").GetProperty("emailConfirmed").GetBoolean().Should().BeTrue();
        passwordStatus.Should().Be(HttpStatusCode.BadRequest);
        refused.GetProperty("code").GetString().Should().Be("Auth.InvalidCredentials");
    }

    [Fact]
    public async Task An_email_google_hasnt_verified_signs_nobody_in()
    {
        var (status, body) = await SignInAsync(factory.Google.Issue(Guid.NewGuid().ToString("N"), $"x-{Guid.NewGuid():N}@example.com", verified: false));

        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("code").GetString().Should().Be("Auth.ExternalEmailNotVerified");
    }

    [Fact]
    public async Task A_refused_code_or_a_token_google_didnt_sign_signs_nobody_in()
    {
        var (refused, refusedBody) = await SignInAsync("a-code-google-refuses");
        var (forged, forgedBody) = await SignInAsync(factory.Google.Issue(Guid.NewGuid().ToString("N"), "victim@gmail.test", forged: true));

        refused.Should().Be(HttpStatusCode.BadRequest);
        refusedBody.GetProperty("code").GetString().Should().Be("Auth.ExternalSignInFailed");
        forged.Should().Be(HttpStatusCode.BadRequest);
        forgedBody.GetProperty("code").GetString().Should().Be("Auth.ExternalSignInFailed");
    }

    [Fact]
    public async Task The_web_app_learns_the_client_id_only_when_google_is_configured()
    {
        var (_, on) = await Api().SendAsync(HttpMethod.Get, "/api/v1/auth/providers");
        using var unconfigured = factory.WithWebHostBuilder(b => b.UseSetting("Google:ClientSecret", ""));
        var (_, off) = await new TestApi(unconfigured.CreateClient(), Ct).SendAsync(HttpMethod.Get, "/api/v1/auth/providers");
        var (status, _) = await new TestApi(unconfigured.CreateClient(), Ct).SendAsync(HttpMethod.Post, "/api/v1/auth/google",
            new { code = "any", codeVerifier = Verifier, redirectUri = Redirect });

        on.GetProperty("googleClientId").GetString().Should().Be(GoogleApiFactory.ClientId);
        off.GetProperty("googleClientId").ValueKind.Should().Be(JsonValueKind.Null);
        status.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> SignInAsync(string code) =>
        Api().SendAsync(HttpMethod.Post, "/api/v1/auth/google", new { code, codeVerifier = Verifier, redirectUri = Redirect });
}

/// <summary>
/// Stands in for Google: publishes an OpenID configuration and its signing key, and redeems codes it issued for ID tokens
/// signed with that key. <see cref="Issue"/> returns a code; a code it never issued is refused like an expired one.
/// </summary>
public sealed class FakeGoogle : HttpMessageHandler
{
    public const string Issuer = "https://accounts.google.test";
    public const string TokenEndpoint = "https://oauth2.google.test/token";

    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "google-test-key" };
    private readonly RsaSecurityKey _otherKey = new(RSA.Create(2048)) { KeyId = "google-test-key" };
    private readonly Dictionary<string, string> _codes = [];

    public string? LastTokenRequest { get; private set; }

    public string Issue(string subject, string email, bool verified = true, string? name = null, bool forged = false)
    {
        var claims = new Dictionary<string, object> { ["email"] = email, ["email_verified"] = verified };
        if (name is not null)
        {
            claims["name"] = name;
        }

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = GoogleApiFactory.ClientId,
            Subject = new ClaimsIdentity([new Claim("sub", subject)]),
            Claims = claims,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(forged ? _otherKey : _key, SecurityAlgorithms.RsaSha256),
        });
        var code = $"code-{Guid.NewGuid():N}";
        lock (_codes)
        {
            _codes[code] = token;
        }

        return code;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        if (url == $"{Issuer}/.well-known/openid-configuration")
        {
            return Json(new { issuer = Issuer, jwks_uri = $"{Issuer}/jwks", token_endpoint = TokenEndpoint });
        }

        if (url == $"{Issuer}/jwks")
        {
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(_key);
            return Json(new { keys = new[] { new { kty = jwk.Kty, kid = jwk.Kid, use = "sig", alg = "RS256", n = jwk.N, e = jwk.E } } });
        }

        var form = await request.Content!.ReadAsStringAsync(cancellationToken);
        LastTokenRequest = form;
        var code = Uri.UnescapeDataString(form.Split('&').Single(p => p.StartsWith("code=", StringComparison.Ordinal))[5..]);
        string? idToken;
        lock (_codes)
        {
            _codes.Remove(code, out idToken); // single use, like Google's
        }

        return idToken is null ? new HttpResponseMessage(HttpStatusCode.BadRequest) : Json(new { id_token = idToken, token_type = "Bearer" });
    }

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
}
