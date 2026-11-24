using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TourDock.Infrastructure.Seeding;

namespace TourDock.Api.IntegrationTests;

/// <summary>
/// A test server can dispose its services while the host is still stopping them (seen on CI in Release builds), so the
/// seeder's StopAsync may run after its Dispose. That must not fail the shutdown.
/// </summary>
public sealed class DemoDataSeederShutdownTests
{
    [Fact]
    public async Task Stopping_after_being_disposed_is_quiet()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var seeder = new DemoDataSeeder(
            services.GetRequiredService<IServiceScopeFactory>(), new ConfigurationBuilder().Build(), NullLogger<DemoDataSeeder>.Instance);

        seeder.Dispose();
        var stop = () => seeder.StopAsync(TestContext.Current.CancellationToken);

        await stop.Should().NotThrowAsync();
        seeder.Dispose(); // twice is fine too
    }
}
