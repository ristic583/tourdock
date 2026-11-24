using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Application.Tours;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Photos;

public sealed record UploadTourPhotoCommand(
    Guid AgencyId, Guid TourId, Stream Content, long Length)
    : IAgencyRequest, ICommand<PhotoDto>, IInvalidatesCatalog;

/// <summary>
/// Upload pipeline: authorize → validate and re-encode (untrusted input never reaches storage as-is) → store variants →
/// record the photo. If recording fails, the stored files are removed again.
/// </summary>
internal sealed class UploadTourPhotoHandler(
    IAgencyAccess access, ITourRepository tours, IImageProcessor images, IFileStorage storage, IPhotoUrls urls, IUnitOfWork unitOfWork)
    : ICommandHandler<UploadTourPhotoCommand, PhotoDto>
{
    public async Task<Result<PhotoDto>> Handle(UploadTourPhotoCommand command, CancellationToken cancellationToken)
    {
        if (command.Length <= 0)
        {
            return ImageErrors.Empty;
        }

        var agency = await access.RequireAsync(command.AgencyId, AgencyAccessLevel.Manage, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        var tour = await tours.GetAsync(command.AgencyId, command.TourId, cancellationToken);
        if (tour is null)
        {
            return TourUseCaseErrors.NotFound;
        }

        if (tour.Photos.Count >= Tour.MaxPhotos)
        {
            return TourErrors.TooManyPhotos; // fail before doing the expensive image work
        }

        var processed = images.Process(command.Content);
        if (processed.IsFailure)
        {
            return processed.Error;
        }

        var photoId = Guid.CreateVersion7();
        var storageKey = $"tours/{tour.Id}/{photoId:N}";
        foreach (var variant in processed.Value.Variants)
        {
            await storage.PutAsync(PhotoVariants.Key(storageKey, variant.Name), variant.Data, "image/webp", cancellationToken);
        }

        try
        {
            var largest = processed.Value.Largest;
            var photo = tour.AddPhoto(photoId, storageKey, largest.Width, largest.Height);
            if (photo.IsFailure)
            {
                await storage.DeleteAsync(PhotoVariants.Keys(storageKey), CancellationToken.None);
                return photo.Error;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return photo.Value.ToDto(urls);
        }
        catch
        {
            await storage.DeleteAsync(PhotoVariants.Keys(storageKey), CancellationToken.None);
            throw;
        }
    }
}
