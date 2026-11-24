using TourDock.Domain.Common;
using TourDock.Domain.Slots;
using TourDock.Domain.Tours;

namespace TourDock.Domain.Bookings;

/// <summary>
/// Coordinates the two aggregates involved in booking: the departure (capacity) and the booking (lifecycle).
/// Callers must persist both in one transaction; the departure's row version prevents concurrent overbooking.
/// </summary>
public static class BookingService
{
    public static Result<Booking> BookSeats(
        Tour tour, Slot slot, Guid customerId, int seats, DateTimeOffset now, string? idempotencyKey = null)
    {
        var check = EnsureBelongs(tour, slot);
        if (check.IsFailure)
        {
            return check.Error;
        }

        var price = tour.PricingOn(slot.LocalDate).PricePerPerson;
        if (price is null)
        {
            return SlotErrors.SharedNotAllowed;
        }

        var reserved = slot.ReserveSeats(seats, now);
        return reserved.IsFailure
            ? reserved.Error
            : Booking.CreatePending(slot.Id, tour, customerId, BookingType.Shared, seats, price, slot.StartsAt, slot.EndsAt, now, idempotencyKey);
    }

    public static Result<Booking> BookPrivate(
        Tour tour, Slot slot, Guid customerId, int groupSize, DateTimeOffset now, string? idempotencyKey = null)
    {
        var check = EnsureBelongs(tour, slot);
        if (check.IsFailure)
        {
            return check.Error;
        }

        var price = tour.PricingOn(slot.LocalDate).PrivatePrice;
        if (price is null)
        {
            return SlotErrors.PrivateNotAllowed;
        }

        var reserved = slot.ReserveWhole(groupSize, now);
        return reserved.IsFailure
            ? reserved.Error
            : Booking.CreatePending(slot.Id, tour, customerId, BookingType.Private, groupSize, price, slot.StartsAt, slot.EndsAt, now, idempotencyKey);
    }

    public static Result Expire(Booking booking, Slot slot, DateTimeOffset now)
    {
        if (booking.SlotId != slot.Id)
        {
            return BookingErrors.SlotMismatch;
        }

        var expired = booking.Expire(now);
        if (expired.IsSuccess)
        {
            Release(booking, slot);
        }

        return expired;
    }

    /// <summary>Cancels a booking and frees its seats. Returns the amount to refund.</summary>
    public static Result<Money> Cancel(Booking booking, Slot slot, CancelledBy by, string? reason, DateTimeOffset now)
    {
        if (booking.SlotId != slot.Id)
        {
            return BookingErrors.SlotMismatch;
        }

        var guests = booking.Guests;
        var refund = booking.Cancel(by, reason, now);
        if (refund.IsSuccess && slot.Status != SlotStatus.Cancelled)
        {
            Release(booking, slot, guests);
        }

        return refund;
    }

    /// <summary>Partial cancellation. Frees the removed seats (shared bookings) and returns the refund.</summary>
    public static Result<Money> ReduceGuests(Booking booking, Slot slot, int newGuests, DateTimeOffset now)
    {
        if (booking.SlotId != slot.Id)
        {
            return BookingErrors.SlotMismatch;
        }

        var removed = booking.Guests - newGuests;
        var refund = booking.ReduceGuests(newGuests, now);
        if (refund.IsSuccess && booking.Type == BookingType.Shared)
        {
            slot.ReleaseSeats(removed);
        }

        return refund;
    }

    private static Result EnsureBelongs(Tour tour, Slot slot)
    {
        if (slot.TourId != tour.Id)
        {
            return BookingErrors.SlotMismatch;
        }

        return tour.IsBookable ? Result.Success() : TourErrors.NotBookable;
    }

    private static void Release(Booking booking, Slot slot, int? guests = null)
    {
        if (booking.Type == BookingType.Private)
        {
            slot.ReleaseWhole();
        }
        else
        {
            slot.ReleaseSeats(guests ?? booking.Guests);
        }
    }
}
