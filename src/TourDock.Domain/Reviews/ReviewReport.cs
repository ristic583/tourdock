using TourDock.Domain.Common;

namespace TourDock.Domain.Reviews;

public enum ReportReason
{
    Spam,
    Offensive,
    PersonalInformation,
    NotAboutTheTour,
    Other,
}

/// <summary>
/// A user flagging a review for an admin to look at. One per user and review. Reports never hide a review by
/// themselves (competitors could abuse that); they only put it in the moderation queue until an admin resolves them.
/// </summary>
public sealed class ReviewReport : Entity
{
    public const int NoteMaxLength = 500;

    private ReviewReport(Guid id)
        : base(id) { }

    private ReviewReport() { }

    public Guid ReviewId { get; private set; }

    public Guid ReporterId { get; private set; }

    public ReportReason Reason { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Set when an admin hid the review or dismissed the reports.</summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

    public static Result<ReviewReport> Create(Review review, Guid reporterId, ReportReason reason, string? note, DateTimeOffset now)
    {
        if (review.CustomerId == reporterId)
        {
            return ReviewReportErrors.OwnReview;
        }

        if (note?.Trim().Length > NoteMaxLength)
        {
            return ReviewReportErrors.NoteTooLong;
        }

        return new ReviewReport(NewId())
        {
            ReviewId = review.Id,
            ReporterId = reporterId,
            Reason = reason,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedAt = now,
        };
    }

    public void Resolve(DateTimeOffset now) => ResolvedAt ??= now;
}

public static class ReviewReportErrors
{
    public static readonly Error OwnReview = Error.Validation("ReviewReport.OwnReview", "You can't report your own review.");

    public static readonly Error NoteTooLong = Error.Validation("ReviewReport.NoteTooLong", $"The note must be at most {ReviewReport.NoteMaxLength} characters.");
}
