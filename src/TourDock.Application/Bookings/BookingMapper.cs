using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Abstractions.Payments;
using TourDock.Application.Common;
using TourDock.Application.Tours;
using TourDock.Domain.Bookings;
using TourDock.Domain.Payments;
using TourDock.Domain.Tours;

namespace TourDock.Application.Bookings;

internal sealed class BookingMapper
{
    public BookingResponse ToResponse(Booking b, Tour tour, DateTimeOffset now, Payment? lastPayment = null) => new(
        b.Id,
        b.TourId,
        tour.Title,
        b.SlotId,
        b.Type,
        b.Guests,
        b.Status,
        b.UnitPrice.ToDto()!,
        b.TotalPrice.ToDto()!,
        b.PaidAmount.ToDto()!,
        b.RefundedAmount.ToDto()!,
        b.SlotStartsAt,
        TimeZoneInfo.ConvertTime(b.SlotStartsAt, tour.Location.TimeZone).DateTime,
        tour.Location.TimeZoneId,
        b.Status == BookingStatus.Pending ? b.HoldExpiresAt : null,
        b.CancellationPolicy.Tiers.Select(t => new RefundTierDto(t.HoursBeforeStart, t.RefundPercent)).ToList(),
        b.CustomerRefundFor(b.TotalPrice, now).ToDto()!,
        b.CreatedAt,
        b.CancelledAt,
        b.CancelledBy,
        b.CancellationReason,
        lastPayment?.Status,
        lastPayment?.Status == PaymentStatus.Failed ? lastPayment.FailureReason : null,
        // When the traveler may still write a review (whether they already did is in GET /me/reviews).
        b.Status == BookingStatus.Completed && b.CompletedAt is { } completed ? completed + Domain.Reviews.Review.ReviewWindow : null);
}
