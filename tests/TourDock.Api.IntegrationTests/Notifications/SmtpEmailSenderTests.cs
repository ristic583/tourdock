using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Options;
using TourDock.Application.Abstractions;
using TourDock.Infrastructure.Email;

namespace TourDock.Api.IntegrationTests.Notifications;

/// <summary>The real SMTP sender against a real Mailpit (same image as compose.yaml), checked through Mailpit's API.</summary>
public sealed class SmtpEmailSenderTests : IAsyncLifetime
{
    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:v1.32.0")
        .WithPortBinding(1025, assignRandomHostPort: true)
        .WithPortBinding(8025, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/api/v1/info")))
        .Build();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _mailpit.StartAsync();

    public async ValueTask DisposeAsync() => await _mailpit.DisposeAsync();

    [Fact]
    public async Task Delivers_text_and_html_with_a_stable_message_id()
    {
        var sender = Sender(skipReserved: false);

        var delivery = await sender.SendAsync(Message("ana@example.com"), Ct);

        delivery.Should().Be(EmailDelivery.Sent);
        var messages = await MessagesAsync();
        messages.GetProperty("total").GetInt32().Should().Be(1);
        var summary = messages.GetProperty("messages")[0];
        summary.GetProperty("Subject").GetString().Should().Be("Booking confirmed: Sunset Cruise");
        summary.GetProperty("MessageID").GetString().Should().Be("n1.traveler@tourdock.test");
        summary.GetProperty("From").GetProperty("Address").GetString().Should().Be("noreply@tourdock.test");

        using var http = Http();
        var full = await http.GetFromJsonAsync<JsonElement>($"/api/v1/message/{summary.GetProperty("ID").GetString()}", Ct);
        full.GetProperty("Text").GetString().Should().Contain("Your booking is confirmed.");
        full.GetProperty("HTML").GetString().Should().Contain("<p>Your booking is confirmed.</p>");
    }

    [Theory]
    [InlineData("ana@example.com")]
    [InlineData("ana@mail.example.org")]
    [InlineData("ana@tourdock.test")]
    [InlineData("ana@demo.invalid")]
    public async Task Never_sends_to_reserved_demo_domains_when_delivering_for_real(string address)
    {
        var delivery = await Sender(skipReserved: true).SendAsync(Message(address), Ct);

        delivery.Should().Be(EmailDelivery.Skipped);
        (await MessagesAsync()).GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Sends_to_real_domains_when_delivering_for_real()
    {
        (await Sender(skipReserved: true).SendAsync(Message("ana@gmail.com"), Ct)).Should().Be(EmailDelivery.Sent);
        (await MessagesAsync()).GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Throws_when_the_server_is_unreachable_so_the_outbox_retries()
    {
        var sender = new SmtpEmailSender(Options.Create(new EmailOptions
        {
            Host = _mailpit.Hostname,
            Port = 1,
            FromAddress = "noreply@tourdock.test",
            TimeoutSeconds = 2,
        }));

        var act = () => sender.SendAsync(Message("ana@gmail.com"), Ct);

        await act.Should().ThrowAsync<Exception>();
    }

    private SmtpEmailSender Sender(bool skipReserved) => new(Options.Create(new EmailOptions
    {
        Host = _mailpit.Hostname,
        Port = _mailpit.GetMappedPublicPort(1025),
        FromAddress = "noreply@tourdock.test",
        SkipReservedDomains = skipReserved,
    }));

    private static EmailMessage Message(string to) =>
        new("n1.traveler", to, "Ana", "Booking confirmed: Sunset Cruise", "Your booking is confirmed.", "<p>Your booking is confirmed.</p>");

    private HttpClient Http() => new() { BaseAddress = new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(8025)}") };

    private async Task<JsonElement> MessagesAsync()
    {
        using var http = Http();
        return await http.GetFromJsonAsync<JsonElement>("/api/v1/messages", Ct);
    }
}
