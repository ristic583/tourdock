using TourDock.Api.Infrastructure;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Reviews;
using TourDock.Domain.Reviews;
using TourDock.Infrastructure;

namespace TourDock.Api.Endpoints;

/// <summary>Reviews: travelers write them, everyone reads them, agencies reply, admins moderate.</summary>
internal static class ReviewEndpoints
{
    public static RouteGroupBuilder MapReviewEndpoints(this RouteGroupBuilder api)
    {
        MapTravelerReviews(api);
        MapPublicReviews(api);
        MapAgencyReviews(api);
        MapReviewModeration(api);
        return api;
    }

    private static void MapTravelerReviews(RouteGroupBuilder api)
    {
        // Traveler: one review per completed booking.
        var mine = api.MapGroup("/bookings/{bookingId:guid}/review").WithTags("Reviews")
            .RequireAuthorization()
            .RequireRateLimiting(Hardening.ReviewPolicy);

        mine.MapPost("/", async (Guid bookingId, ReviewRequest body, ICommandHandler<PostReviewCommand, MyReview> h,
            CancellationToken ct) =>
                (await h.Handle(new PostReviewCommand(bookingId, body.Rating, body.Comment), ct))
                .ToHttp(r => TypedResults.Created($"/api/v1/bookings/{bookingId}/review", r)))
            .Produces<MyReview>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Review your completed booking (once, within 90 days of the trip)");

        mine.MapPut("/", async (Guid bookingId, ReviewRequest body, ICommandHandler<EditReviewCommand, MyReview> h, CancellationToken ct) =>
                (await h.Handle(new EditReviewCommand(bookingId, body.Rating, body.Comment), ct)).ToHttp())
            .Produces<MyReview>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Change your review (within 14 days of posting)");

        mine.MapDelete("/", async (Guid bookingId, ICommandHandler<DeleteReviewCommand, Unit> h, CancellationToken ct) =>
                (await h.Handle(new DeleteReviewCommand(bookingId), ct)).ToHttp(_ => TypedResults.NoContent()))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Delete your review");

        api.MapGet("/me/reviews", async (IQueryHandler<GetMyReviewsQuery, IReadOnlyList<MyReview>> h, CancellationToken ct) =>
                (await h.Handle(new GetMyReviewsQuery(), ct)).ToHttp())
            .RequireAuthorization()
            .WithTags("Reviews")
            .Produces<IReadOnlyList<MyReview>>()
            .WithSummary("Reviews you wrote");

        api.MapPost("/reviews/{reviewId:guid}/reports", async (Guid reviewId, ReportRequest body,
            ICommandHandler<ReportReviewCommand, Unit> h, CancellationToken ct) =>
                (await h.Handle(new ReportReviewCommand(reviewId, body.Reason, body.Note), ct)).ToHttp(_ => TypedResults.NoContent()))
            .RequireAuthorization()
            .RequireRateLimiting(Hardening.ReviewPolicy)
            .WithTags("Reviews")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Report a review to the moderators")
            .WithDescription("Reports never hide a review by themselves; an admin decides. Reporting the same review twice is accepted " +
                "and has no further effect.");
    }

    private static void MapPublicReviews(RouteGroupBuilder api)
    {
        // Public.
        api.MapGet("/tours/{tourId:guid}/reviews", async (Guid tourId, ReviewSort? sort, int? page, int? pageSize,
                IQueryHandler<GetTourReviewsQuery, TourReviewsResponse> h, CancellationToken ct) =>
                (await h.Handle(new GetTourReviewsQuery(tourId, sort ?? ReviewSort.Newest, page ?? 1, pageSize ?? 10), ct)).ToHttp())
            .AllowAnonymous()
            .WithTags("Catalog")
            .Produces<TourReviewsResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Rating summary and visible reviews of a tour");
    }

    private static void MapAgencyReviews(RouteGroupBuilder api)
    {
        // Agency.
        var agency = api.MapGroup("/agencies/{agencyId:guid}/reviews").WithTags("Agency: reviews").RequireAuthorization();

        agency.MapGet("/", async (Guid agencyId, int? page, int? pageSize,
            IQueryHandler<GetAgencyReviewsQuery, PagedResponse<AgencyReview>> h, CancellationToken ct) =>
                (await h.Handle(new GetAgencyReviewsQuery(agencyId, page ?? 1, pageSize ?? 20), ct)).ToHttp())
            .Produces<PagedResponse<AgencyReview>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reviews of your tours, newest first (including hidden ones, marked as such)");

        agency.MapPut("/{reviewId:guid}/reply", async (Guid agencyId, Guid reviewId, ReplyRequest body,
            ICommandHandler<ReplyToReviewCommand, AgencyReview> h, CancellationToken ct) =>
                (await h.Handle(new ReplyToReviewCommand(agencyId, reviewId, body.Text), ct)).ToHttp())
            .Produces<AgencyReview>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Add or change your public reply (managers)");

        agency.MapDelete("/{reviewId:guid}/reply", async (Guid agencyId, Guid reviewId, ICommandHandler<RemoveReviewReplyCommand, Unit> h,
            CancellationToken ct) =>
                (await h.Handle(new RemoveReviewReplyCommand(agencyId, reviewId), ct)).ToHttp(_ => TypedResults.NoContent()))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Remove your reply (managers)");
    }

    private static void MapReviewModeration(RouteGroupBuilder api)
    {
        // Admin.
        var admin = api.MapGroup("/admin").WithTags("Admin").RequireAuthorization(AuthPolicies.Admin);

        admin.MapGet("/reviews", async (bool? reported, bool? hidden, int? page, int? pageSize,
                IQueryHandler<GetReviewsForModerationQuery, PagedResponse<ModerationReview>> h, CancellationToken ct) =>
                (await h.Handle(new GetReviewsForModerationQuery(reported, hidden, page ?? 1, pageSize ?? 20), ct)).ToHttp())
            .Produces<PagedResponse<ModerationReview>>()
            .WithSummary("Moderation queue: most reported first (filter with reported=true / hidden=true)");

        admin.MapPost("/reviews/{reviewId:guid}/hide", async (Guid reviewId, HideRequest body, ICommandHandler<HideReviewCommand, Unit> h,
            CancellationToken ct) =>
                (await h.Handle(new HideReviewCommand(reviewId, body.Reason), ct)).ToHttp(_ => TypedResults.NoContent()))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Hide a review (resolves its open reports; audited)");

        admin.MapPost("/reviews/{reviewId:guid}/unhide", async (Guid reviewId, ICommandHandler<UnhideReviewCommand, Unit> h,
            CancellationToken ct) =>
                (await h.Handle(new UnhideReviewCommand(reviewId), ct)).ToHttp(_ => TypedResults.NoContent()))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Show a hidden review again (audited)");

        admin.MapPost("/reviews/{reviewId:guid}/dismiss-reports", async (Guid reviewId,
            ICommandHandler<DismissReviewReportsCommand, Unit> h, CancellationToken ct) =>
                (await h.Handle(new DismissReviewReportsCommand(reviewId), ct)).ToHttp(_ => TypedResults.NoContent()))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Keep the review and close its reports (audited)");

        admin.MapGet("/audit-log", async (int? page, int? pageSize,
            IQueryHandler<GetAdminActionsQuery, PagedResponse<AdminActionResponse>> h, CancellationToken ct) =>
                (await h.Handle(new GetAdminActionsQuery(page ?? 1, pageSize ?? 50), ct)).ToHttp())
            .Produces<PagedResponse<AdminActionResponse>>()
            .WithSummary("Admin actions, newest first");
    }

    internal sealed record ReviewRequest(int Rating, string? Comment);

    internal sealed record ReportRequest(ReportReason Reason, string? Note);

    internal sealed record ReplyRequest(string Text);

    internal sealed record HideRequest(string Reason);
}
