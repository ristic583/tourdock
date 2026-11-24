using AwesomeAssertions;
using TourDock.Domain.Slots;
using TourDock.Domain.Tours;

namespace TourDock.Domain.UnitTests;

public sealed class SlotTests
{
    [Fact]
    public void Capacity_is_the_smaller_of_tour_and_vehicle_capacity()
    {
        var slot = TestData.Slot(TestData.Tour(maxGuests: 10), vehicleCapacity: 6);

        slot.Capacity.Should().Be(6);
    }

    [Fact]
    public void Cannot_create_a_departure_in_the_past()
    {
        var result = Slot.Create(TestData.Tour(), Guid.NewGuid(), 10, TestData.Now.AddMinutes(-1), TestData.Now);

        result.Error.Should().Be(SlotErrors.StartInPast);
    }

    [Fact]
    public void Cannot_reserve_more_seats_than_remaining()
    {
        var slot = TestData.Slot(TestData.Tour(maxGuests: 10));
        slot.ReserveSeats(8, TestData.Now);

        var result = slot.ReserveSeats(3, TestData.Now);

        result.Error!.Code.Should().Be("Slot.NotEnoughSeats");
        slot.ReservedSeats.Should().Be(8);
    }

    [Fact]
    public void Private_booking_reserves_the_whole_departure()
    {
        var slot = TestData.Slot(TestData.Tour(maxGuests: 10));

        slot.ReserveWhole(4, TestData.Now).IsSuccess.Should().BeTrue();

        slot.RemainingSeats.Should().Be(0);
        slot.ReserveSeats(1, TestData.Now).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Private_booking_is_impossible_once_a_seat_is_sold()
    {
        var slot = TestData.Slot(TestData.Tour());
        slot.ReserveSeats(1, TestData.Now);

        slot.ReserveWhole(4, TestData.Now).Error.Should().Be(SlotErrors.AlreadyPartiallyBooked);
    }

    [Fact]
    public void Shared_only_tour_rejects_private_bookings()
    {
        var slot = TestData.Slot(TestData.Tour(BookingMode.Shared));

        slot.ReserveWhole(2, TestData.Now).Error.Should().Be(SlotErrors.PrivateNotAllowed);
    }

    [Fact]
    public void Cannot_block_a_departure_with_reservations()
    {
        var slot = TestData.Slot(TestData.Tour());
        slot.ReserveSeats(2, TestData.Now);

        slot.Block().Error.Should().Be(SlotErrors.HasReservations);
    }

    [Fact]
    public void Cannot_book_a_departure_that_already_started()
    {
        var slot = TestData.Slot(TestData.Tour(), startsIn: TimeSpan.FromHours(1));

        slot.ReserveSeats(1, TestData.Now.AddHours(2)).Error.Should().Be(SlotErrors.AlreadyStarted);
    }
}
