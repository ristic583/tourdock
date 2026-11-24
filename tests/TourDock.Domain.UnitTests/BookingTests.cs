using AwesomeAssertions;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Reviews;
using TourDock.Domain.Tours;

namespace TourDock.Domain.UnitTests;

public sealed class BookingTests
{
    private static readonly Guid Customer = Guid.NewGuid();

    [Fact]
    public void Shared_booking_holds_seats_and_snapshots_the_price()
    {
        var tour = TestData.Tour(perPerson: 50m);
        var slot = TestData.Slot(tour);

        var booking = BookingService.BookSeats(tour, slot, Customer, 3, TestData.Now).Value;
        tour.UpdatePricing(BookingMode.Both, Money.Eur(99m), Money.Eur(999m)); // later price change

        booking.Status.Should().Be(BookingStatus.Pending);
        booking.TotalPrice.Amount.Should().Be(150m);
        booking.HoldExpiresAt.Should().Be(TestData.Now + Booking.HoldDuration);
        slot.ReservedSeats.Should().Be(3);
    }

    [Fact]
    public void Reference_uses_the_random_tail_of_the_id_like_the_web_app()
    {
        // Same id and expected value as bookingReference() in web/src/lib/my-bookings.test.ts.
        Booking.ReferenceFor(Guid.Parse("01a1b09d-b917-7a89-9fcf-cb8bda1023b4")).Should().Be("TD-DA10-23B4");

        // Two UUIDv7 ids from the same minute share their leading digits but not their reference.
        Booking.ReferenceFor(Guid.Parse("01a1c1d5-6633-7db9-9e32-f671da7df623")).Should()
            .NotBe(Booking.ReferenceFor(Guid.Parse("01a1c1d5-d368-79ab-a57d-392b424ea58e")));
    }

    [Fact]
    public void Booking_an_unpublished_tour_fails()
    {
        var tour = TestData.Tour();
        var slot = TestData.Slot(tour);
        tour.Unpublish();

        BookingService.BookSeats(tour, slot, Customer, 1, TestData.Now).Error.Should().Be(TourErrors.NotBookable);
    }

    [Fact]
    public void A_late_payment_on_an_expired_booking_shows_as_paid_and_refunded()
    {
        var tour = TestData.Tour(perPerson: 50m);
        var slot = TestData.Slot(tour);
        var booking = BookingService.BookSeats(tour, slot, Customer, 2, TestData.Now).Value;
        BookingService.Expire(booking, slot, TestData.Now + Booking.HoldDuration).IsSuccess.Should().BeTrue();

        booking.RecordLatePayment(Money.Eur(100m)).Value.Amount.Should().Be(100m);

        booking.PaidAmount.Amount.Should().Be(100m);
        booking.RefundedAmount.Amount.Should().Be(100m);
    }

    [Fact]
    public void A_late_payment_is_not_recorded_on_an_active_booking()
    {
        var tour = TestData.Tour(perPerson: 50m);
        var booking = BookingService.BookSeats(tour, TestData.Slot(tour), Customer, 1, TestData.Now).Value;

        booking.RecordLatePayment(Money.Eur(50m)).IsFailure.Should().BeTrue();
        booking.RefundedAmount.IsZero.Should().BeTrue();
    }

    [Fact]
    public void Expired_hold_releases_seats()
    {
        var tour = TestData.Tour();
        var slot = TestData.Slot(tour);
        var booking = BookingService.BookSeats(tour, slot, Customer, 2, TestData.Now).Value;

        BookingService.Expire(booking, slot, TestData.Now + Booking.HoldDuration).IsSuccess.Should().BeTrue();

        booking.Status.Should().Be(BookingStatus.Expired);
        slot.ReservedSeats.Should().Be(0);
    }

    [Fact]
    public void Cannot_confirm_after_the_hold_expired()
    {
        var tour = TestData.Tour();
        var booking = BookingService.BookSeats(tour, TestData.Slot(tour), Customer, 1, TestData.Now).Value;

        booking.Confirm(TestData.Now + Booking.HoldDuration).Error.Should().Be(BookingErrors.HoldExpired);
    }

    [Fact]
    public void Customer_cancellation_refunds_according_to_the_policy()
    {
        var tour = TestData.Tour(perPerson: 50m);
        var slot = TestData.Slot(tour, startsIn: TimeSpan.FromHours(48)); // 48h before: 50% tier
        var booking = BookingService.BookSeats(tour, slot, Customer, 2, TestData.Now).Value;
        booking.Confirm(TestData.Now);

        var refund = BookingService.Cancel(booking, slot, CancelledBy.Customer, null, TestData.Now).Value;

        refund.Amount.Should().Be(50m);
        booking.Status.Should().Be(BookingStatus.Cancelled);
        slot.ReservedSeats.Should().Be(0);
    }

    [Fact]
    public void Agency_cancellation_refunds_everything()
    {
        var tour = TestData.Tour(perPerson: 50m);
        var slot = TestData.Slot(tour, startsIn: TimeSpan.FromHours(2));
        var booking = BookingService.BookSeats(tour, slot, Customer, 2, TestData.Now).Value;
        booking.Confirm(TestData.Now);

        BookingService.Cancel(booking, slot, CancelledBy.Agency, "Storm", TestData.Now).Value.Amount.Should().Be(100m);
    }

    [Fact]
    public void Partial_cancellation_releases_seats_and_refunds_the_removed_ones()
    {
        var tour = TestData.Tour(perPerson: 50m);
        var slot = TestData.Slot(tour, startsIn: TimeSpan.FromDays(10)); // full refund tier
        var booking = BookingService.BookSeats(tour, slot, Customer, 5, TestData.Now).Value;
        booking.Confirm(TestData.Now);

        var refund = BookingService.ReduceGuests(booking, slot, 3, TestData.Now).Value;

        refund.Amount.Should().Be(100m);
        booking.Guests.Should().Be(3);
        booking.TotalPrice.Amount.Should().Be(150m);
        slot.ReservedSeats.Should().Be(3);
    }

    [Fact]
    public void Private_booking_reserves_the_departure_at_the_private_price()
    {
        var tour = TestData.Tour(privatePrice: 400m);
        var slot = TestData.Slot(tour);

        var booking = BookingService.BookPrivate(tour, slot, Customer, 4, TestData.Now).Value;

        booking.TotalPrice.Amount.Should().Be(400m);
        slot.RemainingSeats.Should().Be(0);
    }

    [Fact]
    public void Only_completed_bookings_can_be_reviewed()
    {
        var tour = TestData.Tour();
        var slot = TestData.Slot(tour);
        var booking = BookingService.BookSeats(tour, slot, Customer, 1, TestData.Now).Value;
        booking.Confirm(TestData.Now);

        Review.Create(booking, Customer, 5, "Great", TestData.Now).Error.Should().Be(ReviewErrors.BookingNotCompleted);

        booking.Complete(slot.EndsAt);
        Review.Create(booking, Customer, 5, "Great", slot.EndsAt).IsSuccess.Should().BeTrue();
        Review.Create(booking, Guid.NewGuid(), 5, null, slot.EndsAt).Error.Should().Be(ReviewErrors.NotYourBooking);
    }
}
