using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;

namespace TourDock.Api.IntegrationTests;

/// <summary>Changing the email (link to the new address, notice to the old one) and the admin "confirm email" action.</summary>
public sealed partial class EmailChangeTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private TestApi Api() => new(factory.CreateClient(), Ct);

    [Fact]
    public async Task The_email_changes_only_when_the_link_sent_to_the_new_address_is_opened()
    {
        var api = Api();
        var token = await api.RegisterUserAsync();
        var me = (await api.SendAsync(HttpMethod.Get, "/api/v1/me", null, token)).Body;
        var oldEmail = me.GetProperty("email").GetString()!;
        var newEmail = $"moved-{Guid.NewGuid():N}@example.com";

        var requested = await api.SendAsync(HttpMethod.Post, "/api/v1/me/email", new { newEmail, password = TestApi.Password }, token);
        requested.Status.Should().Be(HttpStatusCode.Accepted);
        (await api.SendAsync(HttpMethod.Get, "/api/v1/me", null, token)).Body.GetProperty("email").GetString().Should().Be(oldEmail, "nothing changes before the link");

        await factory.Services.DrainOutboxAsync(Ct);
        factory.Emails.To(oldEmail).Should().ContainSingle(m => m.Subject == "Your TourDock email is about to change")
            .Which.TextBody.Should().Contain("m•••@example.com").And.NotContain(newEmail);
        var link = factory.Emails.To(newEmail).Single(m => m.Subject == "Confirm your new email for TourDock");
        var match = ChangeLink().Match(link.TextBody);
        match.Success.Should().BeTrue(link.TextBody);
        var userId = match.Groups["user"].Value;
        var linkToken = Uri.UnescapeDataString(match.Groups["token"].Value);

        (await api.SendAsync(HttpMethod.Post, "/api/v1/auth/verify-email-change", new { userId, token = linkToken + "x" })).Body
            .GetProperty("code").GetString().Should().Be("Auth.InvalidEmailChangeLink");
        (await api.SendAsync(HttpMethod.Post, "/api/v1/auth/verify-email-change", new { userId, token = linkToken })).Status.Should().Be(HttpStatusCode.NoContent);

        var after = (await api.SendAsync(HttpMethod.Get, "/api/v1/me", null, token)).Body;
        after.GetProperty("email").GetString().Should().Be(newEmail);
        after.GetProperty("emailConfirmed").GetBoolean().Should().BeTrue("opening the link proves the new address");
        (await api.SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { email = newEmail, password = TestApi.Password })).Status.Should().Be(HttpStatusCode.OK);
        (await api.SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { email = oldEmail, password = TestApi.Password })).Status.Should().Be(HttpStatusCode.BadRequest);
        (await api.SendAsync(HttpMethod.Post, "/api/v1/auth/verify-email-change", new { userId, token = linkToken })).Status
            .Should().Be(HttpStatusCode.BadRequest, "the link is single use");
    }

    [Fact]
    public async Task An_address_that_already_has_an_account_gets_the_same_answer_and_its_owner_is_told()
    {
        var api = Api();
        var owner = await api.RegisterUserAsync();
        var taken = (await api.SendAsync(HttpMethod.Get, "/api/v1/me", null, owner)).Body.GetProperty("email").GetString()!;
        var token = await api.RegisterUserAsync();

        var requested = await api.SendAsync(HttpMethod.Post, "/api/v1/me/email", new { newEmail = taken, password = TestApi.Password }, token);

        requested.Status.Should().Be(HttpStatusCode.Accepted, "the same answer as for a free address: no account enumeration");
        await factory.Services.DrainOutboxAsync(Ct);
        var mails = factory.Emails.To(taken);
        mails.Should().ContainSingle(m => m.Subject == "Someone tried to use your email on TourDock");
        mails.Should().NotContain(m => m.Subject == "Confirm your new email for TourDock");
    }

    [Fact]
    public async Task Changing_the_email_needs_the_current_password()
    {
        var api = Api();
        var token = await api.RegisterUserAsync();

        var wrong = await api.SendAsync(HttpMethod.Post, "/api/v1/me/email", new { newEmail = "x@example.com", password = "not-my-password" }, token);

        wrong.Status.Should().Be(HttpStatusCode.BadRequest);
        wrong.Body.GetProperty("code").GetString().Should().Be("Auth.WrongPassword");
        (await api.SendAsync(HttpMethod.Post, "/api/v1/me/email", new { newEmail = "x@example.com", password = TestApi.Password })).Status
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [GeneratedRegex(@"/verify-email\?change=1&user=(?<user>[0-9a-f-]{36})&token=(?<token>[^\s]+)")]
    private static partial Regex ChangeLink();
}

/// <summary>Admin support action, with email confirmation required as in production.</summary>
public sealed class AdminConfirmEmailTests(ConfirmedEmailApiFactory factory) : IClassFixture<ConfirmedEmailApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_admin_can_confirm_an_email_and_it_is_audited()
    {
        var api = new TestApi(factory.CreateClient(), Ct);
        var email = await api.SignUpAsync();
        var admin = await api.AdminTokenAsync();

        (await api.SendAsync(HttpMethod.Post, "/api/v1/admin/users/confirm-email", new { email }, null)).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await api.SendAsync(HttpMethod.Post, "/api/v1/admin/users/confirm-email", new { email = "nobody@example.com" }, admin)).Status.Should().Be(HttpStatusCode.NotFound);
        (await api.SendAsync(HttpMethod.Post, "/api/v1/admin/users/confirm-email", new { email = email.ToUpperInvariant() }, admin)).Status.Should().Be(HttpStatusCode.NoContent);

        var token = await api.LoginAsync(email); // signing in needs the confirmed email
        var me = (await api.SendAsync(HttpMethod.Get, "/api/v1/me", null, token)).Body;
        me.GetProperty("emailConfirmed").GetBoolean().Should().BeTrue();
        (await api.SendAsync(HttpMethod.Post, "/api/v1/admin/users/confirm-email", new { email }, token)).Status.Should().Be(HttpStatusCode.Forbidden);
        var log = (await api.SendAsync(HttpMethod.Get, "/api/v1/admin/audit-log?pageSize=50", null, admin)).Body;
        log.GetProperty("items").EnumerateArray().Should().Contain(a =>
            a.GetProperty("action").GetString() == "User.ConfirmEmail" && a.GetProperty("targetId").GetString() == me.GetProperty("id").GetString()
            && a.GetProperty("targetName").GetString() == "Test User");
    }
}

/// <summary>Sign-up refuses passwords from known breaches (Pwned Passwords range API, stubbed) and fails open when it's down.</summary>
public sealed class BreachedPasswordTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_breached_password_is_refused_and_only_a_hash_prefix_leaves_the_server()
    {
        const string Breached = "summer-holidays-2024";
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(Breached)));
        factory.PasswordsHttp.ReturnText($"0018A45C4D1DEF81644B54AB7F969B88D65:0\r\n{hash[5..]}:1234\r\nFFFFF0000000000000000000000000000AAA:3");
        var api = new TestApi(factory.CreateClient(), Ct);

        var (status, body) = await api.SendAsync(HttpMethod.Post, "/api/v1/auth/register",
            new { email = $"user-{Guid.NewGuid():N}@example.com", password = Breached, displayName = "Ana" });

        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("errors").GetProperty("password")[0].GetString().Should().Contain("data breach");
        var (prefix, suffix) = (hash[..5], hash[5..]);
        factory.PasswordsHttp.Requests.Should().Contain(uri => uri.AbsolutePath == $"/range/{prefix}");
        factory.PasswordsHttp.Requests.Should().NotContain(uri => uri.ToString().Contains(suffix, StringComparison.OrdinalIgnoreCase));

        // A password that isn't in the list (suffix absent, or listed only as padding with count 0) is fine.
        (await api.SendAsync(HttpMethod.Post, "/api/v1/auth/register",
            new { email = $"user-{Guid.NewGuid():N}@example.com", password = "a-long-unlisted-passphrase", displayName = "Ana" })).Status
            .Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task When_the_breach_list_is_down_sign_up_still_works()
    {
        factory.PasswordsHttp.ReturnStatus(HttpStatusCode.ServiceUnavailable);
        var api = new TestApi(factory.CreateClient(), Ct);

        (await api.SendAsync(HttpMethod.Post, "/api/v1/auth/register",
            new { email = $"user-{Guid.NewGuid():N}@example.com", password = "another-long-passphrase", displayName = "Ana" })).Status
            .Should().Be(HttpStatusCode.Accepted);
    }
}

/// <summary>"Resend link" is limited per account, not only per IP.</summary>
public sealed class ResendLimitFactory : TourDockApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimiting:AccountEmailsPerHour", "2");
    }
}

public sealed class ResendLimitTests(ResendLimitFactory factory) : IClassFixture<ResendLimitFactory>
{
    [Fact]
    public async Task A_third_link_within_the_hour_is_refused_for_that_account_only()
    {
        var api = new TestApi(factory.CreateClient(), TestContext.Current.CancellationToken);
        var token = await api.RegisterUserAsync();
        var other = await api.RegisterUserAsync();

        for (var i = 0; i < 2; i++)
        {
            (await api.SendAsync(HttpMethod.Post, "/api/v1/auth/verify-email/resend", null, token)).Status.Should().Be(HttpStatusCode.NoContent);
        }

        var third = await api.SendAsync(HttpMethod.Post, "/api/v1/auth/verify-email/resend", null, token);
        third.Status.Should().Be(HttpStatusCode.TooManyRequests);
        (await api.SendAsync(HttpMethod.Post, "/api/v1/auth/verify-email/resend", null, other)).Status.Should().Be(HttpStatusCode.NoContent);
    }
}
