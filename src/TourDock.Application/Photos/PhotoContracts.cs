using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Common;
using TourDock.Application.Tours;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Photos;

public sealed record PhotoDto(Guid Id, bool IsCover, int Width, int Height, PhotoUrls Urls);

public static class PhotoVariants
{
    /// <summary>Longest side in pixels per variant; images are never upscaled.</summary>
    public static readonly IReadOnlyList<(string Name, int MaxSide)> All = [("thumbnail", 480), ("medium", 1280), ("large", 2048)];

    public static string Key(string storageKey, string variant) => $"{storageKey}/{variant}.webp";

    public static IReadOnlyList<string> Keys(string storageKey) => All.Select(v => Key(storageKey, v.Name)).ToList();
}

public static class PhotoMapping
{
    public static PhotoDto ToDto(this TourPhoto photo, IPhotoUrls urls) =>
        new(photo.Id, photo.IsCover, photo.Width, photo.Height, urls.For(photo.StorageKey));
}
