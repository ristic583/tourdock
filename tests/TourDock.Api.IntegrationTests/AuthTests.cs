using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace TourDock.Api.IntegrationTests;

public sealed class AuthTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private const string Password = "correct-horse-battery";

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Register_is_accepted_without_a_session_and_the_account_can_sign_in()
    {
        using var client = factory.CreateClient();
        var email = NewEmail();

        var (status, body) = await PostAsync(client, "/api/v1/auth/register", new { email, password = Password, displayName = "Ana" });

        status.Should().Be(HttpStatusCode.Accepted);
        body.ValueKind.Should().Be(JsonValueKind.Undefined, "no tokens: sign-up only sends an email");
        var (_, login) = await PostAsync(client, "/api/v1/auth/login", new { email, password = Password });
        var me = await GetAsync(client, "/api/v1/me", login.GetProperty("tokens").GetProperty("accessToken").GetString()!);
        me.GetProperty("email").GetString().Should().Be(email);
    }

    [Fact]
    public async Task Me_requires_authentication()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/me", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Invalid_registration_returns_validation_problem_per_field()
    {
        using var client = factory.CreateClient();

        var (status, body) = await PostAsync(client, "/api/v1/auth/register", new { email = "not-an-email", password = "short", displayName = "" });

        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("code").GetString().Should().Be("Validation.Failed");
        var errors = body.GetProperty("errors");
        errors.TryGetProperty("email", out _).Should().BeTrue();
        errors.TryGetProperty("password", out _).Should().BeTrue();
        errors.TryGetProperty("displayName", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Signing_up_with_a_taken_email_looks_the_same_and_only_the_owner_is_told()
    {
        using var client = factory.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);
        await factory.Services.DrainOutboxAsync(Ct);
        await MarkConfirmedAsync(email);

        var taken = await PostAsync(client, "/api/v1/auth/register", new { email, password = "another-password-456", displayName = "Again" });
        var fresh = await PostAsync(client, "/api/v1/auth/register", new { email = NewEmail(), password = "another-password-456", displayName = "Again" });

        taken.Status.Should().Be(HttpStatusCode.Accepted);
        taken.Should().BeEquivalentTo(fresh, "the answer can't tell whether the address has an account");
        await factory.Services.DrainOutboxAsync(Ct);
        factory.Emails.To(email).Select(m => m.Subject).Should().Contain("Someone tried to sign up with your email on TourDock");
        (await PostAsync(client, "/api/v1/auth/login", new { email, password = Password })).Status.Should().Be(HttpStatusCode.OK, "the password didn't change");
        (await PostAsync(client, "/api/v1/auth/login", new { email, password = "another-password-456" })).Status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_give_the_same_answer()
    {
        using var client = factory.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);

        var wrongPassword = await PostAsync(client, "/api/v1/auth/login", new { email, password = "wrong-password-123" });
        var unknownEmail = await PostAsync(client, "/api/v1/auth/login", new { email = NewEmail(), password = Password });

        wrongPassword.Status.Should().Be(HttpStatusCode.BadRequest);
        unknownEmail.Status.Should().Be(wrongPassword.Status);
        unknownEmail.Body.GetProperty("code").GetString().Should().Be(wrongPassword.Body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Failed_attempts_slow_down_that_address_without_locking_the_owner_out()
    {
        using var client = factory.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);

        for (var i = 0; i < 5; i++)
        {
            await PostAsync(client, "/api/v1/auth/login", new { email, password = "wrong-password-123" }, peer: "198.51.100.66");
        }

        var (attacker, problem) = await PostAsync(client, "/api/v1/auth/login", new { email, password = Password }, peer: "198.51.100.66");
        attacker.Should().Be(HttpStatusCode.TooManyRequests, "the guessing address is throttled, even with the right password");
        problem.GetProperty("code").GetString().Should().Be("Auth.TooManyAttempts");

        var (owner, _) = await PostAsync(client, "/api/v1/auth/login", new { email, password = Password }, peer: "203.0.113.77");
        owner.Should().Be(HttpStatusCode.OK, "the owner signing in from elsewhere is not locked out");
    }

    [Fact]
    public async Task Throttling_looks_the_same_for_unknown_addresses()
    {
        using var client = factory.CreateClient();
        var unknown = NewEmail();
        for (var i = 0; i < 5; i++)
        {
            await PostAsync(client, "/api/v1/auth/login", new { email = unknown, password = "wrong-password-123" }, peer: "198.51.100.67");
        }

        var (status, problem) = await PostAsync(client, "/api/v1/auth/login", new { email = unknown, password = "wrong-password-123" }, peer: "198.51.100.67");

        status.Should().Be(HttpStatusCode.TooManyRequests);
        problem.GetProperty("code").GetString().Should().Be("Auth.TooManyAttempts", "it must not reveal that the address has no account");
    }

    [Fact]
    public async Task Refresh_rotates_tokens_and_reuse_revokes_the_session()
    {
        using var client = factory.CreateClient();
        var original = (await RegisterAsync(client, NewEmail())).GetProperty("refreshToken").GetString()!;

        var (status, rotated) = await PostAsync(client, "/api/v1/auth/refresh", new { refreshToken = original });
        status.Should().Be(HttpStatusCode.OK);
        var next = rotated.GetProperty("refreshToken").GetString()!;
        next.Should().NotBe(original);

        // An attacker replays the old token: rejected, and the legitimate new token dies with it.
        (await PostAsync(client, "/api/v1/auth/refresh", new { refreshToken = original })).Status.Should().Be(HttpStatusCode.BadRequest);
        (await PostAsync(client, "/api/v1/auth/refresh", new { refreshToken = next })).Status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        using var client = factory.CreateClient();
        var refresh = (await RegisterAsync(client, NewEmail())).GetProperty("refreshToken").GetString()!;

        using var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = refresh }, Ct);

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PostAsync(client, "/api/v1/auth/refresh", new { refreshToken = refresh })).Status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Agency_registration_needs_admin_approval()
    {
        using var client = factory.CreateClient();
        var userToken = (await RegisterAsync(client, NewEmail())).GetProperty("accessToken").GetString()!;

        var (created, agency) = await PostAsync(client, "/api/v1/agencies", new { name = "Blue Coast Charters", contactEmail = "hello@example.com" }, userToken);
        created.Should().Be(HttpStatusCode.Created);
        agency.GetProperty("status").GetString().Should().Be("PendingApproval");
        agency.GetProperty("myRole").GetString().Should().Be("Manager");
        var id = agency.GetProperty("id").GetString();

        // A regular user cannot approve...
        (await PostAsync(client, $"/api/v1/admin/agencies/{id}/approve", new { }, userToken)).Status.Should().Be(HttpStatusCode.Forbidden);

        // ...the seeded admin can.
        var (_, adminLogin) = await PostAsync(client, "/api/v1/auth/login", new { email = TourDockApiFactory.AdminEmail, password = TourDockApiFactory.AdminPassword });
        var adminToken = adminLogin.GetProperty("tokens").GetProperty("accessToken").GetString()!;
        var (approved, body) = await PostAsync(client, $"/api/v1/admin/agencies/{id}/approve", new { }, adminToken);

        approved.Should().Be(HttpStatusCode.OK);
        body.GetProperty("status").GetString().Should().Be("Active");
    }

    [Fact]
    public async Task Admin_lists_pending_agencies_and_others_cannot()
    {
        using var client = factory.CreateClient();
        var userToken = (await RegisterAsync(client, NewEmail())).GetProperty("accessToken").GetString()!;
        var (_, agency) = await PostAsync(client, "/api/v1/agencies", new { name = "Pending Harbour Tours", contactEmail = "hi@example.com" }, userToken);
        var (_, adminLogin) = await PostAsync(client, "/api/v1/auth/login", new { email = TourDockApiFactory.AdminEmail, password = TourDockApiFactory.AdminPassword });
        var adminToken = adminLogin.GetProperty("tokens").GetProperty("accessToken").GetString()!;

        var pending = await GetAsync(client, "/api/v1/admin/agencies?status=PendingApproval&pageSize=100", adminToken);

        var row = pending.GetProperty("items").EnumerateArray().Single(a => a.GetProperty("id").GetString() == agency.GetProperty("id").GetString());
        row.GetProperty("status").GetString().Should().Be("PendingApproval");
        row.GetProperty("managerCount").GetInt32().Should().Be(1);

        using var forbidden = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/agencies");
        forbidden.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        (await client.SendAsync(forbidden, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    /// <summary>Signs up and signs in (this factory lets unconfirmed accounts sign in); returns the tokens.</summary>
    private async Task<JsonElement> RegisterAsync(HttpClient client, string email)
    {
        (await PostAsync(client, "/api/v1/auth/register", new { email, password = Password, displayName = "Test" })).Status.Should().Be(HttpStatusCode.Accepted);
        var (status, body) = await PostAsync(client, "/api/v1/auth/login", new { email, password = Password });
        status.Should().Be(HttpStatusCode.OK);
        return body.GetProperty("tokens");
    }

    private async Task MarkConfirmedAsync(string email)
    {
        await using var db = factory.CreateDbContext();
        var user = db.Users.Single(u => u.Email == email);
        user.EmailConfirmed = true;
        await db.SaveChangesAsync(Ct);
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(
        HttpClient client, string url, object payload, string? token = null, string? peer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (peer is not null)
        {
            request.Headers.Add(TourDockApiFactory.PeerHeader, peer);
        }

        using var response = await client.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private async Task<JsonElement> GetAsync(HttpClient client, string url, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
    }
}
