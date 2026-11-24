using Microsoft.EntityFrameworkCore;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Catalog;
using TourDock.Application.Reviews;
using TourDock.Domain.Administration;
using TourDock.Domain.Agencies;
using TourDock.Domain.Reviews;
using TourDock.Domain.Tours;
using TourDock.Infrastructure.Persistence;

namespace TourDock.Infrastructure.Reviews;

internal sealed class ReviewRepository(TourDockDbContext db) : IReviewRepository
{
    public Task<Review?> GetByIdAsync(Guid reviewId, CancellationToken cancellationToken) =>
        db.Reviews.SingleOrDefaultAsync(r => r.Id == reviewId, cancellationToken);

    public Task<Review?> GetByBookingIdAsync(Guid bookingId, CancellationToken cancellationToken) =>
        db.Reviews.SingleOrDefaultAsync(r => r.BookingId == bookingId, cancellationToken);

    public void Add(Review review) => db.Reviews.Add(review);

    public void Remove(Review review) => db.Reviews.Remove(review);

    public Task<bool> HasReportedAsync(Guid reviewId, Guid reporterId, CancellationToken cancellationToken) =>
        db.ReviewReports.AnyAsync(r => r.ReviewId == reviewId && r.ReporterId == reporterId, cancellationToken);

    public void AddReport(ReviewReport report) => db.ReviewReports.Add(report);

    public async Task<IReadOnlyList<ReviewReport>> GetOpenReportsAsync(Guid reviewId, CancellationToken cancellationToken) =>
        await db.ReviewReports.Where(r => r.ReviewId == reviewId && r.ResolvedAt == null).ToListAsync(cancellationToken);
}

internal sealed class AdminAuditLog(TourDockDbContext db) : IAdminAuditLog
{
    public void Add(AdminAction action) => db.AdminActions.Add(action);
}

/// <summary>Read-only projections for review pages. Names are joined from the user table and shortened here.</summary>
internal sealed class ReviewReader(TourDockDbContext db) : IReviewReader
{
    public async Task<TourReviewsResponse?> GetForTourAsync(Guid tourId, ReviewSort sort, int page, int pageSize, CancellationToken cancellationToken)
    {
        var visible = await db.Tours.AsNoTracking()
            .AnyAsync(t => t.Id == tourId && t.Status == TourStatus.Published
                           && db.Agencies.Any(a => a.Id == t.AgencyId && a.Status == AgencyStatus.Active), cancellationToken);
        if (!visible)
        {
            return null;
        }

        var reviews = db.Reviews.AsNoTracking().Where(r => r.TourId == tourId && !r.IsHidden);

        var counts = await reviews.GroupBy(r => r.Rating).Select(g => new { Rating = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        int Stars(int rating) => counts.FirstOrDefault(c => c.Rating == rating)?.Count ?? 0;
        var total = counts.Sum(c => c.Count);
        double? average = total == 0 ? null : Math.Round(counts.Sum(c => (double)c.Rating * c.Count) / total, 1);
        var summary = new RatingSummary(average, total, new StarCounts(Stars(1), Stars(2), Stars(3), Stars(4), Stars(5)));

        var ordered = sort switch
        {
            ReviewSort.Highest => reviews.OrderByDescending(r => r.Rating).ThenByDescending(r => r.CreatedAt),
            ReviewSort.Lowest => reviews.OrderBy(r => r.Rating).ThenByDescending(r => r.CreatedAt),
            _ => reviews.OrderByDescending(r => r.CreatedAt),
        };

        var rows = await ordered.ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new
            {
                r.Id,
                r.Rating,
                r.Comment,
                r.CreatedAt,
                r.EditedAt,
                r.TravelledAt,
                r.AgencyReply,
                r.AgencyReplyAt,
                Author = db.Users.Where(u => u.Id == r.CustomerId).Select(u => u.DisplayName).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new PublicReview(
            r.Id, r.Rating, r.Comment, AuthorNames.Shorten(r.Author), r.CreatedAt, r.EditedAt, r.TravelledAt, r.AgencyReply, r.AgencyReplyAt)).ToList();
        return new TourReviewsResponse(summary, new PagedResponse<PublicReview>(items, page, pageSize, total));
    }

    public async Task<IReadOnlyList<MyReview>> GetForCustomerAsync(Guid customerId, CancellationToken cancellationToken) =>
        await Mine(db.Reviews.Where(r => r.CustomerId == customerId)).ToListAsync(cancellationToken);

    public Task<MyReview?> GetMineAsync(Guid customerId, Guid bookingId, CancellationToken cancellationToken) =>
        Mine(db.Reviews.Where(r => r.CustomerId == customerId && r.BookingId == bookingId)).FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResponse<AgencyReview>> GetForAgencyAsync(Guid agencyId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var reviews = db.Reviews.AsNoTracking().Where(r => r.AgencyId == agencyId);
        var total = await reviews.CountAsync(cancellationToken);
        var items = await ToAgencyReviewsAsync(
            reviews.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize), cancellationToken);
        return new PagedResponse<AgencyReview>(items, page, pageSize, total);
    }

    public async Task<AgencyReview?> GetOneForAgencyAsync(Guid agencyId, Guid reviewId, CancellationToken cancellationToken) =>
        (await ToAgencyReviewsAsync(db.Reviews.AsNoTracking().Where(r => r.AgencyId == agencyId && r.Id == reviewId), cancellationToken))
        .SingleOrDefault();

    private async Task<List<AgencyReview>> ToAgencyReviewsAsync(IQueryable<Review> reviews, CancellationToken cancellationToken)
    {
        var rows = await reviews
            .Select(r => new
            {
                r.Id,
                r.TourId,
                r.Rating,
                r.Comment,
                r.CreatedAt,
                r.TravelledAt,
                r.AgencyReply,
                r.AgencyReplyAt,
                r.IsHidden,
                TourTitle = db.Tours.IgnoreQueryFilters().Where(t => t.Id == r.TourId).Select(t => t.Title).First(),
                Author = db.Users.Where(u => u.Id == r.CustomerId).Select(u => u.DisplayName).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new AgencyReview(r.Id, r.TourId, r.TourTitle, r.Rating, r.Comment, AuthorNames.Shorten(r.Author), r.CreatedAt,
            r.TravelledAt, r.AgencyReply, r.AgencyReplyAt, r.IsHidden)).ToList();
    }

    public async Task<PagedResponse<ModerationReview>> GetForModerationAsync(bool? reported, bool? hidden, int page, int pageSize, CancellationToken cancellationToken)
    {
        var reviews = db.Reviews.AsNoTracking();
        if (reported is { } r)
        {
            reviews = reviews.Where(x => db.ReviewReports.Any(p => p.ReviewId == x.Id && p.ResolvedAt == null) == r);
        }

        if (hidden is { } h)
        {
            reviews = reviews.Where(x => x.IsHidden == h);
        }

        var total = await reviews.CountAsync(cancellationToken);
        var rows = await reviews
            .Select(x => new
            {
                Review = x,
                OpenReports = db.ReviewReports.Count(p => p.ReviewId == x.Id && p.ResolvedAt == null),
                TourTitle = db.Tours.IgnoreQueryFilters().Where(t => t.Id == x.TourId).Select(t => t.Title).First(),
                AgencyName = db.Agencies.Where(a => a.Id == x.AgencyId).Select(a => a.Name).First(),
                Author = db.Users.Where(u => u.Id == x.CustomerId).Select(u => u.DisplayName).FirstOrDefault(),
            })
            // Most reported first, then newest: the queue shows what needs attention at the top.
            .OrderByDescending(x => x.OpenReports).ThenByDescending(x => x.Review.CreatedAt).ThenBy(x => x.Review.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        var ids = rows.Select(x => x.Review.Id).ToList();
        var reasons = await db.ReviewReports.AsNoTracking()
            .Where(p => ids.Contains(p.ReviewId) && p.ResolvedAt == null)
            .Select(p => new { p.ReviewId, p.Reason })
            .ToListAsync(cancellationToken);

        var items = rows.Select(x => new ModerationReview(
            x.Review.Id, x.Review.TourId, x.TourTitle, x.AgencyName, x.Review.Rating, x.Review.Comment, x.Author ?? "Traveler",
            x.Review.CreatedAt, x.Review.AgencyReply, x.Review.IsHidden, x.Review.HiddenReason, x.OpenReports,
            reasons.Where(p => p.ReviewId == x.Review.Id).GroupBy(p => p.Reason).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList())).ToList();
        return new PagedResponse<ModerationReview>(items, page, pageSize, total);
    }

    public async Task<PagedResponse<AdminActionResponse>> GetAdminActionsAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var total = await db.AdminActions.CountAsync(cancellationToken);
        var rows = await db.AdminActions.AsNoTracking()
            .OrderByDescending(a => a.At).ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new
            {
                Action = a,
                AdminName = db.Users.Where(u => u.Id == a.AdminId).Select(u => u.DisplayName).FirstOrDefault(),
                AgencyName = db.Agencies.Where(g => g.Id == a.TargetId).Select(g => g.Name).FirstOrDefault(),
                UserName = a.TargetType == "User" ? db.Users.Where(u => u.Id == a.TargetId).Select(u => u.DisplayName).FirstOrDefault() : null,
                Review = db.Reviews.IgnoreQueryFilters().Where(r => r.Id == a.TargetId)
                    .Select(r => new
                    {
                        r.TourId,
                        r.Rating,
                        TourTitle = db.Tours.IgnoreQueryFilters().Where(t => t.Id == r.TourId).Select(t => t.Title).FirstOrDefault(),
                    })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return new PagedResponse<AdminActionResponse>(
            rows.Select(x => new AdminActionResponse(x.Action.Id, x.AdminName ?? "Admin", x.Action.Action, x.Action.TargetType, x.Action.TargetId,
                x.Action.Reason, x.Action.At,
                TargetName: x.AgencyName ?? x.UserName ?? (x.Review is null ? null : $"{x.Review.Rating}★ review of “{x.Review.TourTitle}”"),
                TourId: x.Review?.TourId)).ToList(),
            page, pageSize, total);
    }

    private IQueryable<MyReview> Mine(IQueryable<Review> reviews) =>
        reviews.AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new MyReview(
                r.Id, r.BookingId, r.TourId,
                db.Tours.IgnoreQueryFilters().Where(t => t.Id == r.TourId).Select(t => t.Title).First(),
                r.Rating, r.Comment, r.CreatedAt, r.EditedAt, r.CreatedAt + Review.EditWindow, r.AgencyReply, r.AgencyReplyAt, r.IsHidden));
}
