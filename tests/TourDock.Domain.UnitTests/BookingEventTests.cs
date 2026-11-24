using AwesomeAssertions;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;

namespace TourDock.Domain.UnitTests;

public sealed class BookingEventTests
{
    private static readonly Guid Customer = Guid.NewGuid();

    [Fact]
    public void Confirming_raises_booking_confirmed()
    {
        var booking = Pending(guests: 2);

        booking.Confirm(TestData.Now).IsSuccess.Should().BeTrue();

        booking.DomainEvents.Should().Equal(new BookingConfirmed(booking.Id));
    }

    [Fact]
    public void Customer_cancellation_carries_the_policy_refund()
    {
        var tour = TestData.Tour(perPerson: 50m);
        var slot = TestData.Slot(tour, startsIn: TimeSpan.FromDays(7));
        var booking = BookingService.BookSeats(tour, slot, Customer, 2, TestData.Now).Value;
        booking.Confirm(TestData.Now);
        booking.ClearDomainEvents();

        BookingService.Cancel(booking, slot, CancelledBy.Customer, null, TestData.Now).IsSuccess.Should().BeTrue();

        booking.DomainEvents.Should().Equal(new BookingCancelled(booking.Id, CancelledBy.Customer, Money.Eur(100m)));
    }

    [Fact]
    public void Reducing_guests_reports_removed_guests_and_refund()
    {
        var tour = TestData.Tour(perPerson: 50m);
        var slot = TestData.Slot(tour);
        var booking = BookingService.BookSeats(tour, slot, Customer, 3, TestData.Now).Value;
        booking.Confirm(TestData.Now);
        booking.ClearDomainEvents();

        BookingService.ReduceGuests(booking, slot, 1, TestData.Now).IsSuccess.Should().BeTrue();

        booking.DomainEvents.Should().Equal(new BookingGuestsReduced(booking.Id, 2, Money.Eur(100m)));
    }

    [Fact]
    public void Failed_or_silent_transitions_raise_nothing()
    {
        var tour = TestData.Tour();
        var slot = TestData.Slot(tour);
        var booking = BookingService.BookSeats(tour, slot, Customer, 1, TestData.Now).Value;

        booking.Confirm(TestData.Now + Booking.HoldDuration).IsFailure.Should().BeTrue(); // hold expired
        BookingService.Expire(booking, slot, TestData.Now + Booking.HoldDuration).IsSuccess.Should().BeTrue();

        booking.DomainEvents.Should().BeEmpty("an expired hold doesn't email anyone");
    }

    private static Booking Pending(int guests)
    {
        var tour = TestData.Tour();
        return BookingService.BookSeats(tour, TestData.Slot(tour), Customer, guests, TestData.Now).Value;
    }
}
