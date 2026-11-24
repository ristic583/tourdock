using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TourDock.Domain.Bookings;
using TourDock.Domain.Payments;
using TourDock.Infrastructure.Outbox;

namespace TourDock.Infrastructure.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Version).IsRowVersion();
        builder.Property(p => p.ProviderPaymentId).HasMaxLength(100);
        builder.Property(p => p.CheckoutUrl).HasMaxLength(500);
        builder.Property(p => p.FailureReason).HasMaxLength(500);
        builder.MapMoney(p => p.Amount, "amount");
        builder.HasOne<Booking>().WithMany().HasForeignKey(p => p.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.BookingId, p.Status });
        builder.HasIndex(p => p.ProviderPaymentId).IsUnique().HasFilter("provider_payment_id IS NOT NULL");

        // At most one payment in progress per booking.
        builder.HasIndex(p => p.BookingId).IsUnique().HasFilter("status = 'Pending'").HasDatabaseName("ux_payments_one_pending_per_booking");
    }
}

internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("refunds", t => t.HasCheckConstraint("ck_refunds_amount_positive", "amount_amount > 0"));
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Version).IsRowVersion();
        builder.Property(r => r.Reason).HasMaxLength(200);
        builder.Property(r => r.ProviderRefundId).HasMaxLength(100);
        builder.Property(r => r.FailureReason).HasMaxLength(500);
        builder.MapMoney(r => r.Amount, "amount");
        builder.HasOne<Payment>().WithMany().HasForeignKey(r => r.PaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.BookingId);
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(200);
        builder.Property(m => m.Payload).HasColumnType("jsonb");
        builder.Property(m => m.LastError).HasMaxLength(1000);

        // The worker only ever looks at unprocessed messages that are due.
        builder.HasIndex(m => new { m.NextAttemptAt, m.OccurredAt }).HasFilter("processed_at IS NULL");
    }
}

internal sealed class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable("processed_events");
        builder.HasKey(e => new { e.Source, e.EventId });
        builder.Property(e => e.Source).HasMaxLength(50);
        builder.Property(e => e.EventId).HasMaxLength(100);
    }
}

internal sealed class SentNotificationConfiguration : IEntityTypeConfiguration<SentNotification>
{
    public void Configure(EntityTypeBuilder<SentNotification> builder)
    {
        builder.ToTable("sent_notifications");
        builder.HasKey(n => n.Key);
        builder.Property(n => n.Key).HasMaxLength(100);
        builder.Property(n => n.Delivery).HasMaxLength(20);
    }
}
