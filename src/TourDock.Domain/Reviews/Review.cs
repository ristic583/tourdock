using TourDock.Domain.Bookings;
using TourDock.Domain.Common;

namespace TourDock.Domain.Reviews;

/// <summary>
/// A customer's rating of a tour. Only for their own completed booking, once per booking, within
/// <see cref="ReviewWindow"/> of the trip. The author can edit it for <see cref="EditWindow"/> and delete it any time;
/// the agency can reply publicly but never change or hide it; only an admin can hide it.
/// </summary>
public sealed class Review : Entity, IAuditable
{
    public const int CommentMaxLength = 2000;
    public const int ReplyMaxLength = 1000;
    public const int HiddenReasonMaxLength = 500;

    public static readonly TimeSpan ReviewWindow = TimeSpan.FromDays(90);
    public static readonly TimeSpan EditWindow = TimeSpan.FromDays(14);

    private Review(Guid id)
        : base(id) { }

    private Review() { }

    public Guid BookingId { get; private set; }

    public Guid TourId { get; private set; }

    public Guid AgencyId { get; private set; }

    public Guid CustomerId { get; private set; }

    /// <summary>1 to 5 stars.</summary>
    public int Rating { get; private set; }

    public string? Comment { get; private set; }

    /// <summary>When the trip took place (the departure's start), shown as "Travelled June 2026".</summary>
    public DateTimeOffset TravelledAt { get; private set; }

    /// <summary>When the author last changed the rating or comment (null if never).</summary>
    public DateTimeOffset? EditedAt { get; private set; }

    /// <summary>The agency's public answer.</summary>
    public string? AgencyReply { get; private set; }

    public DateTimeOffset? AgencyReplyAt { get; private set; }

    /// <summary>Hidden by an admin (moderation). Hidden reviews are kept but not shown publicly or counted in ratings.</summary>
    public bool IsHidden { get; private set; }

    public string? HiddenReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<Review> Create(Booking booking, Guid customerId, int rating, string? comment, DateTimeOffset now)
    {
        if (booking.CustomerId != customerId)
        {
            return ReviewErrors.NotYourBooking;
        }

        if (booking.Status != BookingStatus.Completed || booking.CompletedAt is null)
        {
            return ReviewErrors.BookingNotCompleted;
        }

        if (now > booking.CompletedAt.Value + ReviewWindow)
        {
            return ReviewErrors.ReviewWindowClosed;
        }

        var valid = Validate(rating, comment);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        var review = new Review(NewId())
        {
            BookingId = booking.Id,
            TourId = booking.TourId,
            AgencyId = booking.AgencyId,
            CustomerId = customerId,
            Rating = rating,
            Comment = Clean(comment),
            TravelledAt = booking.SlotStartsAt,
            CreatedAt = now,
        };
        review.Raise(new ReviewPosted(review.Id, booking.Id));
        return review;
    }

    public Result Edit(int rating, string? comment, DateTimeOffset now)
    {
        if (now > CreatedAt + EditWindow)
        {
            return ReviewErrors.EditWindowClosed;
        }

        var valid = Validate(rating, comment);
        if (valid.IsFailure)
        {
            return valid;
        }

        Rating = rating;
        Comment = Clean(comment);
        EditedAt = now;
        return Result.Success();
    }

    /// <summary>Adds or replaces the agency's public reply.</summary>
    public Result Reply(string text, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ReviewErrors.ReplyRequired;
        }

        if (text.Trim().Length > ReplyMaxLength)
        {
            return ReviewErrors.ReplyTooLong;
        }

        AgencyReply = text.Trim();
        AgencyReplyAt = now;
        return Result.Success();
    }

    public void RemoveReply()
    {
        AgencyReply = null;
        AgencyReplyAt = null;
    }

    public Result Hide(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > HiddenReasonMaxLength)
        {
            return ReviewErrors.HiddenReasonRequired;
        }

        IsHidden = true;
        HiddenReason = reason.Trim();
        return Result.Success();
    }

    public void Unhide()
    {
        IsHidden = false;
        HiddenReason = null;
    }

    private static Result Validate(int rating, string? comment)
    {
        if (rating is < 1 or > 5)
        {
            return ReviewErrors.InvalidRating;
        }

        return comment?.Trim().Length > CommentMaxLength ? ReviewErrors.CommentTooLong : Result.Success();
    }

    private static string? Clean(string? comment) => string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
}

/// <summary>A new review was posted (the agency is told about it).</summary>
public sealed record ReviewPosted(Guid ReviewId, Guid BookingId) : IDomainEvent;

public static class ReviewErrors
{
    public static readonly Error NotFound = Error.NotFound("Review.NotFound", "Review not found.");

    public static readonly Error NotYourBooking = Error.Forbidden("Review.NotYourBooking", "You can only review your own bookings.");

    public static readonly Error BookingNotCompleted =
        Error.Conflict("Review.BookingNotCompleted", "Only completed bookings can be reviewed.");

    public static readonly Error ReviewWindowClosed =
        Error.Conflict("Review.WindowClosed", $"Reviews can be written up to {Review.ReviewWindow.TotalDays:0} days after the trip.");

    public static readonly Error EditWindowClosed =
        Error.Conflict("Review.EditWindowClosed", $"A review can be changed for {Review.EditWindow.TotalDays:0} days after posting.");

    public static readonly Error InvalidRating = Error.Validation("Review.InvalidRating", "Rating must be between 1 and 5.");

    public static readonly Error CommentTooLong =
        Error.Validation("Review.CommentTooLong", $"Comment must be at most {Review.CommentMaxLength} characters.");

    public static readonly Error ReplyRequired = Error.Validation("Review.ReplyRequired", "The reply can't be empty.");

    public static readonly Error ReplyTooLong =
        Error.Validation("Review.ReplyTooLong", $"The reply must be at most {Review.ReplyMaxLength} characters.");

    public static readonly Error HiddenReasonRequired =
        Error.Validation("Review.HiddenReasonRequired", $"Give a reason (at most {Review.HiddenReasonMaxLength} characters).");

    public static readonly Error AlreadyReviewed = Error.Conflict("Review.AlreadyReviewed", "This booking has already been reviewed.");
}
