using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Domain.Bookings;

/// <summary>Booking payment state: confirming on payment, late payments, and refund previews.</summary>
public sealed partial class Booking
{
    /// <summary>Called when payment succeeds.</summary>
    public Result Confirm(DateTimeOffset now)
    {
        if (Status != BookingStatus.Pending)
        {
            return BookingErrors.InvalidStatus(Status, "confirmed");
        }

        if (IsHoldExpired(now))
        {
            return BookingErrors.HoldExpired;
        }

        Status = BookingStatus.Confirmed;
        ConfirmedAt = now;
        PaidAmount = TotalPrice;
        Raise(new BookingConfirmed(Id));
        return Result.Success();
    }

    /// <summary>
    /// Confirms a booking whose payment succeeded. Unlike <see cref="Confirm"/>, a payment that completes shortly after the
    /// hold time is accepted as long as the booking is still pending: its seats are still reserved, and the customer paid.
    /// </summary>
    public Result ConfirmPaid(Money amountPaid, DateTimeOffset now)
    {
        if (Status != BookingStatus.Pending)
        {
            return BookingErrors.InvalidStatus(Status, "confirmed");
        }

        Status = BookingStatus.Confirmed;
        ConfirmedAt = now;
        PaidAmount = amountPaid;
        Raise(new BookingConfirmed(Id));
        return Result.Success();
    }

    /// <summary>
    /// The payment arrived after the booking was cancelled or its hold expired. The money was taken and goes straight
    /// back, so the booking records both: what the customer sees ("paid, refunded") matches what happened to their money.
    /// Returns the amount to refund.
    /// </summary>
    public Result<Money> RecordLatePayment(Money amountPaid)
    {
        if (Status is not (BookingStatus.Cancelled or BookingStatus.Expired))
        {
            return BookingErrors.InvalidStatus(Status, "refunded as a late payment");
        }

        PaidAmount = PaidAmount.Add(amountPaid);
        RefundedAmount = RefundedAmount.Add(amountPaid);
        return amountPaid;
    }

    /// <summary>The refund a customer would get if they cancelled now (used for previews and for cancelling).</summary>
    public Money CustomerRefundFor(Money amount, DateTimeOffset now) =>
        Status == BookingStatus.Confirmed
            ? amount.Percentage(CancellationPolicy.RefundPercentFor(SlotStartsAt - now))
            : Money.Zero(amount.Currency);
}
