using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Domain.Common;
using TourDock.Domain.Reviews;

namespace TourDock.Application.Reviews;

public enum ReviewSort
{
    Newest,
    Highest,
    Lowest,
}

/// <summary>How many visible reviews gave each number of stars.</summary>
public sealed record StarCounts(int One, int Two, int Three, int Four, int Five);

/// <param name="Average">Average of visible reviews, rounded to one decimal; null without reviews.</param>
public sealed record RatingSummary(double? Average, int Count, StarCounts Distribution);

/// <param name="AuthorName">Shortened for privacy, e.g. "Ana M.".</param>
/// <param name="TravelledAt">Start of the departure the author was on (shown as "Travelled June 2026").</param>
public sealed record PublicReview(
    Guid Id,
    int Rating,
    string? Comment,
    string AuthorName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    DateTimeOffset TravelledAt,
    string? AgencyReply,
    DateTimeOffset? AgencyReplyAt);

public sealed record TourReviewsResponse(RatingSummary Summary, PagedResponse<PublicReview> Reviews);

/// <param name="EditableUntil">The author can change rating and comment until then.</param>
public sealed record MyReview(
    Guid Id,
    Guid BookingId,
    Guid TourId,
    string TourTitle,
    int Rating,
    string? Comment,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    DateTimeOffset EditableUntil,
    string? AgencyReply,
    DateTimeOffset? AgencyReplyAt,
    bool IsHidden);

public sealed record AgencyReview(
    Guid Id,
    Guid TourId,
    string TourTitle,
    int Rating,
    string? Comment,
    string AuthorName,
    DateTimeOffset CreatedAt,
    DateTimeOffset TravelledAt,
    string? AgencyReply,
    DateTimeOffset? AgencyReplyAt,
    bool IsHidden);

/// <param name="OpenReports">Reports an admin hasn't resolved yet.</param>
/// <param name="ReportReasons">Reasons given in the open reports, most common first.</param>
public sealed record ModerationReview(
    Guid Id,
    Guid TourId,
    string TourTitle,
    string AgencyName,
    int Rating,
    string? Comment,
    string AuthorName,
    DateTimeOffset CreatedAt,
    string? AgencyReply,
    bool IsHidden,
    string? HiddenReason,
    int OpenReports,
    IReadOnlyList<ReportReason> ReportReasons);

/// <param name="TargetName">What the action was about, readable: the agency's name, or the tour a review is on.</param>
/// <param name="TourId">For a review, its tour (so the log can link to it).</param>
public sealed record AdminActionResponse(
    Guid Id, string AdminName, string Action, string TargetType, Guid TargetId, string? Reason, DateTimeOffset At, string? TargetName = null, Guid? TourId = null);

/// <summary>Read side for reviews (implemented in Infrastructure with projections).</summary>
public interface IReviewReader
{
    /// <summary>Null when the tour isn't publicly visible.</summary>
    Task<TourReviewsResponse?> GetForTourAsync(Guid tourId, ReviewSort sort, int page, int pageSize, CancellationToken cancellationToken);

    Task<IReadOnlyList<MyReview>> GetForCustomerAsync(Guid customerId, CancellationToken cancellationToken);

    Task<MyReview?> GetMineAsync(Guid customerId, Guid bookingId, CancellationToken cancellationToken);

    Task<PagedResponse<AgencyReview>> GetForAgencyAsync(Guid agencyId, int page, int pageSize, CancellationToken cancellationToken);

    Task<AgencyReview?> GetOneForAgencyAsync(Guid agencyId, Guid reviewId, CancellationToken cancellationToken);

    Task<PagedResponse<ModerationReview>> GetForModerationAsync(bool? reported, bool? hidden, int page, int pageSize, CancellationToken cancellationToken);

    Task<PagedResponse<AdminActionResponse>> GetAdminActionsAsync(int page, int pageSize, CancellationToken cancellationToken);
}

public static class AuthorNames
{
    /// <summary>"Ana Marić" → "Ana M."; a single name stays as it is. Other travelers never see full names.</summary>
    public static string Shorten(string? displayName)
    {
        var parts = (displayName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "Traveler",
            1 => parts[0],
            _ => $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.",
        };
    }
}

public static class AdminActions
{
    public const string ReviewHide = "Review.Hide";
    public const string ReviewUnhide = "Review.Unhide";
    public const string ReviewDismissReports = "Review.DismissReports";
    public const string AgencyApprove = "Agency.Approve";
    public const string AgencySuspend = "Agency.Suspend";
    public const string UserConfirmEmail = "User.ConfirmEmail";
}

internal static class Paging
{
    public const int MaxPageSize = 50;
}

public static class ReviewUseCaseErrors
{
    public static readonly Error BookingNotFound = Error.NotFound("Booking.NotFound", "Booking not found.");
}
