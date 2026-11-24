using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TourDock.Infrastructure;

/// <summary>
/// Deployments set <c>Deployment:RejectLocalSecrets=true</c>. The API then refuses to start while any secret still has
/// one of the local-only defaults from compose.yaml, which are public in the repository. Local runs are unaffected.
/// </summary>
public sealed class DeploymentOptions
{
    public const string SectionName = "Deployment";

    public bool RejectLocalSecrets { get; init; }
}

internal static class LocalSecretsGuard
{
    private static readonly string[] SecretKeys =
    [
        "Jwt:SigningKey", "Payments:ApiKey", "Payments:WebhookSecret", "Storage:AccessKey", "Storage:SecretKey",
        $"ConnectionStrings:{DependencyInjection.DatabaseConnectionStringName}",
    ];

    // The defaults in compose.yaml all contain this marker, except the storage access key.
    private const string LocalMarker = "local-dev-only";
    private const string LocalStorageAccessKey = "tourdock-local";

    public static void AddLocalSecretsGuard(this IServiceCollection services, IConfiguration configuration) =>
        services.AddOptions<DeploymentOptions>()
            .Bind(configuration.GetSection(DeploymentOptions.SectionName))
            .Validate(
                o => !o.RejectLocalSecrets || LocalDefaults(configuration).Count == 0,
                $"Local-only default secrets in a deployment: {string.Join(", ", LocalDefaults(configuration))}. Set real values.")
            .ValidateOnStart();

    /// <summary>Names (never values) of the settings that still hold a local-only default.</summary>
    internal static List<string> LocalDefaults(IConfiguration configuration) =>
        SecretKeys.Where(key => configuration[key] is { } value
                && (value.Contains(LocalMarker, StringComparison.OrdinalIgnoreCase) || value == LocalStorageAccessKey))
            .ToList();
}
