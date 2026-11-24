using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Common;
using TourDock.Application.Tours;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Photos;

/// <summary>Outbox message: delete a removed photo's files from storage (retried until it succeeds).</summary>
public sealed record DeletePhotoFilesMessage(IReadOnlyList<string> Keys) : IOutboxMessage;

internal sealed class DeletePhotoFilesHandler(IFileStorage storage) : IOutboxMessageHandler<DeletePhotoFilesMessage>
{
    public Task HandleAsync(DeletePhotoFilesMessage message, CancellationToken cancellationToken) =>
        storage.DeleteAsync([.. message.Keys], cancellationToken);
}
