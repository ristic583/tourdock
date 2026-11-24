using System.ComponentModel.DataAnnotations;

namespace TourDock.Infrastructure.Files;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>S3 endpoint the API talks to (inside Docker: the storage container).</summary>
    [Required]
    [Url]
    public string ServiceUrl { get; init; } = string.Empty;

    /// <summary>Public base URL browsers use to load photos, including the bucket (in production: a CDN in front of the bucket).</summary>
    [Required]
    [Url]
    public string PublicBaseUrl { get; init; } = string.Empty;

    [Required]
    public string Bucket { get; init; } = "tour-photos";

    [Required]
    public string AccessKey { get; init; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string SecretKey { get; init; } = string.Empty;
}
