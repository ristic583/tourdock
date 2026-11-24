using System.ComponentModel.DataAnnotations;

namespace TourDock.Infrastructure.Payments;

public sealed class PaymentsOptions
{
    public const string SectionName = "Payments";

    /// <summary>Base URL of the provider API (the sandbox container in local environments).</summary>
    [Required]
    [Url]
    public string BaseUrl { get; init; } = string.Empty;

    [Required]
    [MinLength(16)]
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Shared secret used to verify webhook signatures.</summary>
    [Required]
    [MinLength(32)]
    public string WebhookSecret { get; init; } = string.Empty;

    /// <summary>
    /// Origins (scheme://host[:port]) the provider may send customers back to after checkout, e.g. the web app's
    /// public origin. Empty means return URLs are rejected.
    /// </summary>
    public string[] AllowedReturnOrigins { get; init; } = [];

    /// <summary>Webhooks older than this are rejected (replay protection).</summary>
    public int WebhookToleranceSeconds { get; init; } = 300;
}
