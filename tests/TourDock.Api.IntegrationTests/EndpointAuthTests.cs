using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace TourDock.Api.IntegrationTests;

/// <summary>
/// Convention S3: every API endpoint states who may call it, either RequireAuthorization(...) or AllowAnonymous().
/// A new endpoint that forgets both fails here instead of silently inheriting whatever the default is.
/// </summary>
public sealed class EndpointAuthTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    [Fact]
    public void Every_api_endpoint_declares_its_authorization()
    {
        using var client = factory.CreateClient(); // starts the app so the endpoints are built
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true
                        || e.RoutePattern.RawText?.StartsWith("/mcp", StringComparison.Ordinal) == true)
            .ToList();

        var undeclared = endpoints
            .Where(e => e.Metadata.GetMetadata<IAuthorizeData>() is null && e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Select(e => $"{string.Join(",", e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])} {e.RoutePattern.RawText}")
            .ToList();

        // Both at once means anonymous: AllowAnonymous wins over RequireAuthorization.
        var contradictory = endpoints
            .Where(e => e.Metadata.GetMetadata<IAuthorizeData>() is not null && e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText)
            .ToList();

        endpoints.Should().HaveCountGreaterThan(50);
        undeclared.Should().BeEmpty();
        contradictory.Should().BeEmpty();
    }
}
