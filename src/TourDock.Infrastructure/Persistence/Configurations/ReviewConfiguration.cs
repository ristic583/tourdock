using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TourDock.Domain.Administration;
using TourDock.Domain.Bookings;
using TourDock.Domain.Reviews;
using TourDock.Domain.Tours;
using TourDock.Infrastructure.Identity;

namespace TourDock.Infrastructure.Persistence.Configurations;

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("reviews", t => t.HasCheckConstraint("ck_reviews_rating", "rating BETWEEN 1 AND 5"));
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Comment).HasMaxLength(Review.CommentMaxLength);
        builder.Property(r => r.AgencyReply).HasMaxLength(Review.ReplyMaxLength);
        builder.Property(r => r.HiddenReason).HasMaxLength(Review.HiddenReasonMaxLength);

        builder.HasOne<Booking>().WithOne().HasForeignKey<Review>(r => r.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tour>().WithMany().HasForeignKey(r => r.TourId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.BookingId).IsUnique();
        builder.HasIndex(r => new { r.TourId, r.CreatedAt }).HasFilter("is_hidden = false");
        builder.HasIndex(r => new { r.AgencyId, r.CreatedAt });
    }
}

internal sealed class ReviewReportConfiguration : IEntityTypeConfiguration<ReviewReport>
{
    public void Configure(EntityTypeBuilder<ReviewReport> builder)
    {
        builder.ToTable("review_reports");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Reason).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.Note).HasMaxLength(ReviewReport.NoteMaxLength);

        // Reports go with the review when its author deletes it.
        builder.HasOne<Review>().WithMany().HasForeignKey(r => r.ReviewId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.ReporterId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.ReviewId, r.ReporterId }).IsUnique();
        builder.HasIndex(r => r.ReviewId).HasFilter("resolved_at IS NULL").HasDatabaseName("ix_review_reports_open");
    }
}

internal sealed class AdminActionConfiguration : IEntityTypeConfiguration<AdminAction>
{
    public void Configure(EntityTypeBuilder<AdminAction> builder)
    {
        builder.ToTable("admin_audit_log");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Action).HasMaxLength(64);
        builder.Property(a => a.TargetType).HasMaxLength(64);
        builder.Property(a => a.Reason).HasMaxLength(AdminAction.ReasonMaxLength);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(a => a.AdminId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.At);
        builder.HasIndex(a => new { a.TargetType, a.TargetId });
    }
}
