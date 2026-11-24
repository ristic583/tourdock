using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TourDock.Domain.Agencies;
using TourDock.Domain.Bookings;
using TourDock.Domain.Slots;
using TourDock.Domain.Tours;
using TourDock.Infrastructure.Identity;

namespace TourDock.Infrastructure.Persistence.Configurations;

internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public const string IdempotencyIndexName = "ux_bookings_customer_idempotency_key";

    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings", t => t.HasCheckConstraint("ck_bookings_guests", "guests >= 1"));
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Version).IsRowVersion();
        builder.Property(b => b.CancellationReason).HasMaxLength(500);
        builder.Property(b => b.IdempotencyKey).HasMaxLength(64);

        builder.MapMoney(b => b.UnitPrice, "unit_price");
        builder.MapMoney(b => b.TotalPrice, "total_price");
        builder.MapMoney(b => b.PaidAmount, "paid_amount");
        builder.MapMoney(b => b.RefundedAmount, "refunded_amount");
        builder.MapCancellationPolicy(b => b.CancellationPolicy);

        builder.HasOne<Slot>().WithMany().HasForeignKey(b => b.SlotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tour>().WithMany().HasForeignKey(b => b.TourId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Agency>().WithMany().HasForeignKey(b => b.AgencyId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(b => b.CustomerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(b => b.SlotId);
        builder.HasIndex(b => new { b.CustomerId, b.SlotStartsAt });
        builder.HasIndex(b => new { b.AgencyId, b.SlotStartsAt });

        // The same Idempotency-Key from the same customer can only ever create one booking.
        builder.HasIndex(b => new { b.CustomerId, b.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName(IdempotencyIndexName)
            .HasFilter("idempotency_key IS NOT NULL");

        // Lets the jobs find stale holds and finished bookings cheaply.
        builder.HasIndex(b => b.SlotEndsAt).HasFilter("status = 'Confirmed'");
        builder.HasIndex(b => b.HoldExpiresAt).HasFilter("status = 'Pending'");
    }
}
