using TourDock.Domain.Common;

namespace TourDock.Domain.Tours;

/// <summary>A processed photo of a tour. The image files live in object storage under <see cref="StorageKey"/>.</summary>
public sealed class TourPhoto : Entity
{
    internal TourPhoto(Guid id, Guid tourId, string storageKey, int width, int height, int sortOrder)
        : base(id)
    {
        TourId = tourId;
        StorageKey = storageKey;
        Width = width;
        Height = height;
        SortOrder = sortOrder;
    }

    private TourPhoto() { }

    public Guid TourId { get; private set; }

    /// <summary>Key prefix in object storage; size variants are stored below it (e.g. "{key}/medium.webp").</summary>
    public string StorageKey { get; private set; } = null!;

    /// <summary>Dimensions of the largest stored variant.</summary>
    public int Width { get; private set; }

    public int Height { get; private set; }

    public int SortOrder { get; internal set; }

    public bool IsCover { get; internal set; }
}
