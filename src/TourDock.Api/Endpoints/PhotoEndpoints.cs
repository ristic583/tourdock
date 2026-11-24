using Microsoft.AspNetCore.Mvc;
using TourDock.Api.Infrastructure;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Photos;
using TourDock.Application.Tours;

namespace TourDock.Api.Endpoints;

internal static class PhotoEndpoints
{
    private const long MaxRequestBytes = ImageErrors.MaxBytes + (64 * 1024); // file + multipart overhead

    public static RouteGroupBuilder MapPhotoEndpoints(this RouteGroupBuilder api)
    {
        var photos = api.MapGroup("/agencies/{agencyId:guid}/tours/{tourId:guid}/photos").WithTags("Agency: photos").RequireAuthorization();

        photos.MapPost("/", async (Guid agencyId, Guid tourId, HttpRequest request, ICommandHandler<UploadTourPhotoCommand, PhotoDto> h,
            CancellationToken ct) =>
            {
                // Read the form ourselves so an oversized upload is reported as 413, not as a generic binding error.
                if (request.ContentLength > MaxRequestBytes)
                {
                    return TooLarge();
                }

                IFormCollection form;
                try
                {
                    form = await request.ReadFormAsync(ct);
                }
                catch (Exception ex) when (ex is InvalidDataException
                    or BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
                {
                    return TooLarge();
                }

                if (form.Files.GetFile("file") is not { } file)
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["A file is required."] });
                }

                await using var content = file.OpenReadStream();
                return (await h.Handle(new UploadTourPhotoCommand(agencyId, tourId, content, file.Length), ct))
                    .ToHttp(p => TypedResults.Created($"/api/v1/agencies/{agencyId}/tours/{tourId}", p));
            })
            .Accepts<IFormFile>("multipart/form-data")
            // JWT bearer auth (no cookies), so cross-site form posts can't ride on a session: antiforgery isn't needed here.
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = ImageErrors.MaxBytes })
            .WithSummary("Upload a JPEG, PNG or WebP (max 10 MB). Stored as resized WebP variants without metadata.");

        photos.MapDelete("/{photoId:guid}", async (Guid agencyId, Guid tourId, Guid photoId,
            ICommandHandler<DeleteTourPhotoCommand, Unit> h, CancellationToken ct) =>
            (await h.Handle(new DeleteTourPhotoCommand(agencyId, tourId, photoId), ct)).ToHttp(_ => TypedResults.NoContent()));

        photos.MapPut("/{photoId:guid}/cover", async (Guid agencyId, Guid tourId, Guid photoId,
            ICommandHandler<SetCoverPhotoCommand, TourResponse> h, CancellationToken ct) =>
            (await h.Handle(new SetCoverPhotoCommand(agencyId, tourId, photoId), ct)).ToHttp());

        photos.MapPut("/order", async (Guid agencyId, Guid tourId, ReorderRequest body,
            ICommandHandler<ReorderPhotosCommand, TourResponse> h, CancellationToken ct) =>
            (await h.Handle(new ReorderPhotosCommand(agencyId, tourId, body.PhotoIds), ct)).ToHttp());

        return api;
    }

    private static IResult TooLarge() => TypedResults.Problem(
        statusCode: StatusCodes.Status413PayloadTooLarge, title: "Payload too large", detail: "Photos can be at most 10 MB.");

    internal sealed record ReorderRequest(IReadOnlyList<Guid> PhotoIds);
}
