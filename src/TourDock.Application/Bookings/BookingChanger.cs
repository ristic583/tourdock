using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Payments;
using TourDock.Application.Common;
using TourDock.Application.Tours;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Slots;

namespace TourDock.Application.Bookings;

/// <summary>Shared flow: load booking + departure, check access, apply a domain change, save; retried on conflicts.</summary>
internal sealed class BookingChanger(
    IBookingRepository bookings, ISlotRepository slots, ITourRepository tours, IPaymentRepository payments,
    Payments.RefundScheduler refundScheduler, IUnitOfWork unitOfWork, BookingMapper mapper, TimeProvider time)
{
    public Task<Result<CancellationResult>> ChangeAsync(
        Guid bookingId, Func<Booking, bool> canAccess, Func<Booking, Slot, DateTimeOffset, Result<Money>> change,
        CancellationToken cancellationToken) =>
        ConcurrencyRetry.RunAsync(unitOfWork, async () =>
        {
            var booking = await bookings.GetByIdAsync(bookingId, cancellationToken);
            if (booking is null || !canAccess(booking))
            {
                return BookingUseCaseErrors.NotFound;
            }

            var slot = await slots.GetByIdAsync(booking.SlotId, cancellationToken);
            if (slot is null)
            {
                return BookingUseCaseErrors.DepartureNotFound;
            }

            var now = time.GetUtcNow();
            var refund = change(booking, slot, now);
            if (refund.IsFailure)
            {
                return refund.Error;
            }

            // The refund is recorded and queued in the same transaction as the cancellation.
            await refundScheduler.ScheduleForBookingAsync(booking, refund.Value, "Booking cancelled or reduced", cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            var tour = (await tours.GetManyForDisplayAsync([booking.TourId], cancellationToken))[booking.TourId];
            var payment = (await payments.GetLatestForBookingsAsync([booking.Id], cancellationToken)).GetValueOrDefault(booking.Id);
            return Result.Success(new CancellationResult(mapper.ToResponse(booking, tour, now, payment), refund.Value.ToDto()!));
        });
}
