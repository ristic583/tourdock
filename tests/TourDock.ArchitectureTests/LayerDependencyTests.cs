using System.Reflection;
using AwesomeAssertions;
using NetArchTest.Rules;

namespace TourDock.ArchitectureTests;

/// <summary>
/// Enforces the Clean Architecture dependency rule: dependencies only point inwards
/// (Api -> Infrastructure -> Application -> Domain).
/// </summary>
public sealed class LayerDependencyTests
{
    private const string Domain = "TourDock.Domain";
    private const string Application = "TourDock.Application";
    private const string Infrastructure = "TourDock.Infrastructure";
    private const string Api = "TourDock.Api";

    public static TheoryData<string, string[]> ForbiddenDependencies => new()
    {
        { Domain, [Application, Infrastructure, Api] },
        { Application, [Infrastructure, Api] },
        { Infrastructure, [Api] },
    };

    [Theory]
    [MemberData(nameof(ForbiddenDependencies))]
    public void Layer_does_not_depend_on_outer_layers(string layer, string[] forbidden)
    {
        var result = Types.InAssembly(Assembly.Load(layer))
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "{0} must not depend on {1}, but these types do: {2}",
            layer,
            string.Join(", ", forbidden),
            string.Join(", ", result.FailingTypeNames ?? []));
    }
}
