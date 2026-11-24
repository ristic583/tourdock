using System.Net;
using AwesomeAssertions;
using TourDock.Infrastructure.Outbox;

namespace TourDock.Api.IntegrationTests.Operations;

public sealed class OutboxHealthTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Parked_messages_make_readiness_degraded_but_still_ready()
    {
        using var client = factory.CreateClient();
        (await (await client.GetAsync("/health/ready", Ct)).Content.ReadAsStringAsync(Ct)).Should().Be("Healthy");

        await using (var db = factory.CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            db.OutboxMessages.Add(new OutboxMessage
            {
                Type = "Test.Message",
                Payload = "{}",
                OccurredAt = now.AddHours(-1),
                NextAttemptAt = now,
                ProcessedAt = now,
                Attempts = OutboxProcessor.MaxAttempts,
                LastError = "SMTP down",
            });
            await db.SaveChangesAsync(Ct);
        }

        using var response = await client.GetAsync("/health/ready", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "late emails are worth a look, not a reason to stop serving");
        (await response.Content.ReadAsStringAsync(Ct)).Should().Be("Degraded");
    }
}
