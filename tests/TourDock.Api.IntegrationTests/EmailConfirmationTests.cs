using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TourDock.Infrastructure.Outbox;

namespace TourDock.Api.IntegrationTests;

/// <summary>The API with email confirmation required, as in production.</summary>
public sealed class ConfirmedEmailApiFactory : TourDockApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Accounts:RequireConfirmedEmail", "true");
    }
}

/// <summary>Sign-up → emailed link → confirmed → signed in, and why an unconfirmed account can't sign in yet.</summary>
public sealed partial class EmailConfirmationTests(ConfirmedEmailApiFactory factory) : IClassFixture<ConfirmedEmailApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_unconfirmed_account_cannot_sign_in_until_it_opens_the_link()
    {
        var api = Api();
        var departure = await api.CreateDepartureAsync(); // its manager confirms through the link (Api())
        var email = await api.SignUpAsync();
        var (userId, linkToken) = await LinkSentToAsync(email);

        var refused = await api.SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { email, password = TestApi.Password });
        refused.Status.Should().Be(HttpStatusCode.Forbidden);
        refused.Body.GetProperty("code").GetString().Should().Be("Auth.SignInNeedsConfirmedEmail");
        refused.Body.TryGetProperty("tokens", out _).Should().BeFalse();
        var (_, newest) = await LinkSentToAsync(email, skip: 1); // signing in with the right password sends a fresh link
        newest.Should().NotBe(linkToken);

        (await api.SendAsync(HttpMethod.Post, "/api/v1/auth/verify-email", new { userId, token = newest })).Status.Should().Be(HttpStatusCode.NoContent);
        (await api.SendAsync(HttpMethod.Post, "/api/v1/auth/verify-email", new { userId, token = newest })).Status
            .Should().Be(HttpStatusCode.NoContent, "opening the link twice is fine");

        var token = await api.LoginAsync(email);
        (await api.SendAsync(HttpMethod.Get, "/api/v1/me", null, token)).Body.GetProperty("emailConfirmed").GetBoolean().Should().BeTrue();
        (await api.BookAsync(token, departure.DepartureId, 1)).Status.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_wrong_password_on_an_unconfirmed_account_sends_nothing_and_looks_like_any_wrong_password()
    {
        var api = Api();
        var email = await api.SignUpAsync();
        await LinkSentToAsync(email);

        var wrong = await api.SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { email, password = "wrong-password-123" });

        wrong.Status.Should().Be(HttpStatusCode.BadRequest);
        wrong.Body.GetProperty("code").GetString().Should().Be("Auth.InvalidCredentials");
        await factory.Services.DrainOutboxAsync(Ct);
        factory.Emails.To(email).Should().ContainSingle();
    }

    [Fact]
    public async Task Signing_up_again_before_confirming_sends_a_new_link_and_keeps_the_first_password()
    {
        var api = Api();
        var email = await api.SignUpAsync();
        await api.SignUpAsync(email, password: "a-different-password-789");

        var (userId, second) = await LinkSentToAsync(email, skip: 1);
        (await api.SendAsync(HttpMethod.Post, "/api/v1/auth/verify-email", new { userId, token = second })).Status.Should().Be(HttpStatusCode.NoContent);

        (await api.SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { email, password = "a-different-password-789" })).Status
            .Should().Be(HttpStatusCode.BadRequest, "a sign-up never changes an existing account's password");
        await api.LoginAsync(email);
    }

    [Fact]
    public async Task A_wrong_or_replaced_token_is_refused()
    {
        var api = Api();
        var email = await api.SignUpAsync();
        var (userId, first) = await LinkSentToAsync(email);

        var wrong = await api.SendAsync(HttpMethod.Post, "/api/v1/auth/verify-email", new { userId, token = first + "x" });
        wrong.Status.Should().Be(HttpStatusCode.BadRequest);
        wrong.Body.GetProperty("code").GetString().Should().Be("Auth.InvalidConfirmationLink");

        // Asking for a new link (by signing in) replaces the old one.
        (await api.SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { email, password = TestApi.Password })).Status.Should().Be(HttpStatusCode.Forbidden);
        var (_, second) = await LinkSentToAsync(email, skip: 1);
        second.Should().NotBe(first);

        (await api.SendAsync(HttpMethod.Post, "/api/v1/auth/verify-email", new { userId, token = first })).Status.Should().Be(HttpStatusCode.BadRequest);
        (await api.SendAsync(HttpMethod.Post, "/api/v1/auth/verify-email", new { userId, token = second })).Status.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task The_email_is_branded_html_with_a_plain_text_twin_and_only_a_hash_is_stored()
    {
        var api = Api();
        var email = await api.SignUpAsync();
        var (_, linkToken) = await LinkSentToAsync(email);

        var message = factory.Emails.To(email).Single();
        message.Subject.Should().Be("Confirm your email for TourDock");
        message.HtmlBody.Should().Contain("TourDock").And.Contain("Confirm my email").And.Contain("role=\"presentation\"");
        message.TextBody.Should().Contain("Confirm your email: https://");

        await using var db = factory.CreateDbContext();
        db.UserTokens.Where(t => t.Name == "EmailConfirmation").Select(t => t.Value).ToList().Should().NotContain(value => value!.Contains(linkToken));
    }

    /// <summary>A client whose helper accounts (agency managers) confirm their email through the real link.</summary>
    private TestApi Api()
    {
        TestApi api = null!;
        api = new TestApi(factory.CreateClient(), Ct)
        {
            OnRegistered = async email =>
            {
                var (userId, linkToken) = await LinkSentToAsync(email);
                (await api.SendAsync(HttpMethod.Post, "/api/v1/auth/verify-email", new { userId, token = linkToken })).Status.Should().Be(HttpStatusCode.NoContent);
            },
        };
        return api;
    }

    /// <summary>Runs the outbox and returns the user id and token from the newest unread confirmation email.</summary>
    private async Task<(string UserId, string Token)> LinkSentToAsync(string email, int skip = 0)
    {
        await factory.Services.DrainOutboxAsync(Ct);
        var mail = factory.Emails.To(email).Where(m => m.Subject == "Confirm your email for TourDock").Skip(skip).First();
        var match = LinkPattern().Match(mail.TextBody);
        match.Success.Should().BeTrue(mail.TextBody);
        return (match.Groups["user"].Value, Uri.UnescapeDataString(match.Groups["token"].Value));
    }

    [GeneratedRegex(@"/verify-email\?user=(?<user>[0-9a-f-]{36})&token=(?<token>[^\s]+)")]
    private static partial Regex LinkPattern();
}
