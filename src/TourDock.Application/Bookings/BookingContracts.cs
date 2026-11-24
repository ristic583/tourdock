using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Abstractions.Payments;
using TourDock.Application.Common;
using TourDock.Application.Tours;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Payments;

namespace TourDock.Application.Bookings;

/// <param name="LastPaymentStatus">Status of the most recent payment attempt; null if payment was never started.</param>
/// <param name="LastPaymentFailureReason">Why the most recent attempt failed (e.g. "Card declined"), when it did.</param>
public sealed record BookingResponse(
    Guid Id,
    Guid TourId,
    string TourTitle,
    Guid DepartureId,
    BookingType Type,
    int Guests,
    BookingStatus Status,
    MoneyDto UnitPrice,
    MoneyDto TotalPrice,
    MoneyDto PaidAmount,
    MoneyDto RefundedAmount,
    DateTimeOffset StartsAt,
    DateTime LocalStart,
    string TimeZoneId,
    DateTimeOffset? HoldExpiresAt,
    IReadOnlyList<RefundTierDto> CancellationPolicy,
    MoneyDto RefundIfCancelledNow,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CancelledAt,
    CancelledBy? CancelledBy,
    string? CancellationReason,
    PaymentStatus? LastPaymentStatus,
    string? LastPaymentFailureReason,
    DateTimeOffset? ReviewableUntil = null);

/// <summary>Booking as the agency sees it: includes who booked (needed to run the tour).</summary>
public sealed record AgencyBookingResponse(BookingResponse Booking, string CustomerName, string CustomerEmail);

/// <param name="Replayed">True when the same Idempotency-Key was sent before and the original booking is returned.</param>
public sealed record CreateBookingResult(BookingResponse Booking, bool Replayed);

public sealed record CancellationResult(BookingResponse Booking, MoneyDto Refund);

public static class BookingUseCaseErrors
{
    public static readonly Error NotFound = Error.NotFound("Booking.NotFound", "Booking not found.");

    public static readonly Error DepartureNotFound = Error.NotFound("Departure.NotFound", "Departure not found.");

    public static readonly Error IdempotencyKeyReused = Error.Conflict(
        "Booking.IdempotencyKeyReused", "This Idempotency-Key was already used for a different booking request.");
}
