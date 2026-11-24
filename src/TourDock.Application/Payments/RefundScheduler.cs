using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Payments;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Payments;

namespace TourDock.Application.Payments;

/// <summary>Records a refund and queues it in the outbox, inside the caller's transaction.</summary>
internal sealed class RefundScheduler(IPaymentRepository payments, IRefundRepository refunds, IOutbox outbox)
{
    /// <summary>Refunds part of what a booking's customer paid (no-op if nothing was paid or the amount is zero).</summary>
    public async Task ScheduleForBookingAsync(Booking booking, Money amount, string reason, CancellationToken cancellationToken)
    {
        if (amount.IsZero)
        {
            return;
        }

        var payment = await payments.GetSucceededForBookingAsync(booking.Id, cancellationToken);
        if (payment is not null)
        {
            await ScheduleAsync(payment, amount, reason, cancellationToken);
        }
    }

    public async Task ScheduleAsync(Payment payment, Money amount, string reason, CancellationToken cancellationToken)
    {
        var refunded = await refunds.SumForPaymentAsync(payment.Id, payment.Amount.Currency, cancellationToken);
        var refund = Refund.Create(payment, amount, reason, refunded);
        refunds.Add(refund);
        outbox.Enqueue(new IssueRefundMessage(refund.Id));
    }
}
