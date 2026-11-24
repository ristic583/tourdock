using TourDock.Domain.Common;

namespace TourDock.Application.Abstractions;

public sealed record ImageVariant(string Name, byte[] Data, int Width, int Height);

/// <summary>Variants of one uploaded image, re-encoded as WebP without metadata.</summary>
public sealed record ProcessedImage(IReadOnlyList<ImageVariant> Variants)
{
    public ImageVariant Largest => Variants.MaxBy(v => v.Width * v.Height)!;
}

/// <summary>Validates and re-encodes untrusted image uploads.</summary>
public interface IImageProcessor
{
    /// <summary>
    /// Detects the real format from the file content (never the file name or declared content type), checks limits,
    /// applies the camera orientation, strips all metadata (EXIF/GPS) and produces resized WebP variants.
    /// </summary>
    Result<ProcessedImage> Process(Stream content);
}

/// <summary>Object storage (S3-compatible).</summary>
public interface IFileStorage
{
    Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken);

    Task DeleteAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken);
}

public sealed record PhotoUrls(string Thumbnail, string Medium, string Large);

/// <summary>Builds public (CDN-friendly) URLs for stored photo variants.</summary>
public interface IPhotoUrls
{
    PhotoUrls For(string storageKey);
}

public static class ImageErrors
{
    public static readonly Error UnsupportedFormat =
        Error.Validation("Photo.UnsupportedFormat", "Only JPEG, PNG and WebP images are accepted.");

    public static readonly Error TooSmall =
        Error.Validation("Photo.TooSmall", $"The image must be at least {MinShortSide} pixels on its shorter side.");

    public static readonly Error TooLarge =
        Error.Validation("Photo.TooLarge", $"The image must be at most {MaxSide} pixels on each side.");

    public static readonly Error Empty = Error.Validation("Photo.Empty", "The file is empty.");

    public const int MinShortSide = 600;
    public const int MaxSide = 10_000;
    public const long MaxBytes = 10 * 1024 * 1024;
}
