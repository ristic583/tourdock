using System.Reflection;
using AwesomeAssertions;
using NetArchTest.Rules;
using TourDock.Application.Abstractions.Messaging;

namespace TourDock.ArchitectureTests;

/// <summary>Where code may live (docs/conventions.md §1): endpoints never reach the database, business code knows no frameworks.</summary>
public sealed class PlacementTests
{
    public static TheoryData<string, string[]> ForbiddenFrameworks => new()
    {
        { "TourDock.Application", ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore"] },
        { "TourDock.Domain", ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions"] },
    };

    [Theory]
    [MemberData(nameof(ForbiddenFrameworks))]
    public void Layer_does_not_use_forbidden_namespaces(string layer, string[] forbidden)
    {
        var result = Types.InAssembly(Assembly.Load(layer)).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();

        result.IsSuccessful.Should().BeTrue("{0} must not use {1}: {2}", layer, string.Join(", ", forbidden), string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Endpoints_call_handlers_and_never_touch_the_database()
    {
        // The exception handler may translate database exceptions into HTTP answers; endpoints may not query or save.
        var result = Types.InAssembly(Assembly.Load("TourDock.Api")).That().ResideInNamespace("TourDock.Api.Endpoints")
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql", "TourDock.Infrastructure.Persistence", "TourDock.Infrastructure.Repositories")
            .GetResult();

        result.IsSuccessful.Should().BeTrue("endpoints using the database: {0}", string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Handlers_are_internal_and_reached_only_through_their_interfaces()
    {
        var result = Types.InAssembly(typeof(ICommand<>).Assembly)
            .That().ImplementInterface(typeof(ICommandHandler<,>)).Or().ImplementInterface(typeof(IQueryHandler<,>))
            .ShouldNot().BePublic()
            .GetResult();

        result.IsSuccessful.Should().BeTrue("public handlers: {0}", string.Join(", ", result.FailingTypeNames ?? []));
    }
}
