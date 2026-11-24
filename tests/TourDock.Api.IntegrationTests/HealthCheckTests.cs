using System.Net;
using AwesomeAssertions;

namespace TourDock.Api.IntegrationTests;

public sealed class HealthCheckTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoint_reports_healthy(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("Healthy");
    }
}
