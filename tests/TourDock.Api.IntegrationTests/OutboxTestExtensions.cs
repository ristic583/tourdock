using Microsoft.Extensions.DependencyInjection;
using TourDock.Infrastructure.Outbox;

namespace TourDock.Api.IntegrationTests;

public static class OutboxTestExtensions
{
    /// <summary>
    /// Processes outbox batches until one comes back empty. Tests share a database, so other tests' messages (agency
    /// approvals, sign-ups) can fill a single batch before the message a test is waiting for.
    /// </summary>
    public static async Task DrainOutboxAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        var processor = services.GetRequiredService<OutboxProcessor>();
        for (var batch = 0; batch < 50 && await processor.ProcessBatchAsync(cancellationToken) > 0; batch++)
        {
        }
    }
}
