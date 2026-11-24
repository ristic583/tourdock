using TourDock.Application.Abstractions.Data;
using TourDock.Application.Bookings;
using TourDock.Application.Common;
using TourDock.Domain.Common;
using TourDock.Domain.Payments;

namespace TourDock.Application.Payments;

public sealed record PaymentResponse(Guid PaymentId, Guid BookingId, PaymentStatus Status, MoneyDto Amount, string? CheckoutUrl);

public static class PaymentErrors
{
    public static readonly Error NotPayable = Error.Conflict("Payment.BookingNotPayable", "Only a pending booking can be paid.");

    public static readonly Error UnknownPayment = Error.NotFound("Payment.Unknown", "Unknown payment.");

    public static readonly Error UnknownRefund = Error.NotFound("Refund.Unknown", "Unknown refund.");
}

public enum PaymentEventType
{
    PaymentSucceeded,
    PaymentFailed,
    RefundSucceeded,
    RefundFailed,
}

internal static class PaymentMapping
{
    public static PaymentResponse ToResponse(this Payment p) => new(p.Id, p.BookingId, p.Status, p.Amount.ToDto()!, p.CheckoutUrl);
}
