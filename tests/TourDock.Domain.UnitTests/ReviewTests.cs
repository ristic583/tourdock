using AwesomeAssertions;
using TourDock.Domain.Bookings;
using TourDock.Domain.Reviews;

namespace TourDock.Domain.UnitTests;

public sealed class ReviewTests
{
    private static readonly Guid Customer = Guid.NewGuid();

    [Fact]
    public void Review_snapshots_the_trip_and_announces_itself()
    {
        var (booking, completedAt) = CompletedBooking();

        var review = Review.Create(booking, Customer, 4, "  Lovely crew  ", completedAt).Value;

        review.Comment.Should().Be("Lovely crew");
        review.TravelledAt.Should().Be(booking.SlotStartsAt);
        review.DomainEvents.Should().Equal(new ReviewPosted(review.Id, booking.Id));
    }

    [Fact]
    public void Reviews_close_ninety_days_after_the_trip()
    {
        var (booking, completedAt) = CompletedBooking();

        Review.Create(booking, Customer, 5, null, completedAt + Review.ReviewWindow).IsSuccess.Should().BeTrue();
        Review.Create(booking, Customer, 5, null, completedAt + Review.ReviewWindow + TimeSpan.FromSeconds(1))
            .Error.Should().Be(ReviewErrors.ReviewWindowClosed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Rating_must_be_one_to_five(int rating)
    {
        var (booking, completedAt) = CompletedBooking();

        Review.Create(booking, Customer, rating, null, completedAt).Error.Should().Be(ReviewErrors.InvalidRating);
    }

    [Fact]
    public void Comment_has_a_length_limit()
    {
        var (booking, completedAt) = CompletedBooking();

        Review.Create(booking, Customer, 5, new string('a', Review.CommentMaxLength + 1), completedAt).Error.Should().Be(ReviewErrors.CommentTooLong);
    }

    [Fact]
    public void Author_can_edit_for_fourteen_days()
    {
        var (booking, completedAt) = CompletedBooking();
        var review = Review.Create(booking, Customer, 3, "OK", completedAt).Value;

        review.Edit(5, "Better than I thought", completedAt + TimeSpan.FromDays(13)).IsSuccess.Should().BeTrue();
        review.Rating.Should().Be(5);
        review.EditedAt.Should().NotBeNull();
        review.Edit(1, null, completedAt + Review.EditWindow + TimeSpan.FromMinutes(1)).Error.Should().Be(ReviewErrors.EditWindowClosed);
    }

    [Fact]
    public void Agency_reply_and_admin_hide()
    {
        var (booking, completedAt) = CompletedBooking();
        var review = Review.Create(booking, Customer, 2, "Late start", completedAt).Value;

        review.Reply("   ", completedAt).Error.Should().Be(ReviewErrors.ReplyRequired);
        review.Reply("Sorry, the harbour was closed that morning.", completedAt).IsSuccess.Should().BeTrue();
        review.AgencyReply.Should().Be("Sorry, the harbour was closed that morning.");

        review.Hide("").Error.Should().Be(ReviewErrors.HiddenReasonRequired);
        review.Hide("Contains a phone number").IsSuccess.Should().BeTrue();
        review.IsHidden.Should().BeTrue();
        review.Unhide();
        review.IsHidden.Should().BeFalse();
        review.HiddenReason.Should().BeNull();
    }

    [Fact]
    public void Authors_cannot_report_their_own_review()
    {
        var (booking, completedAt) = CompletedBooking();
        var review = Review.Create(booking, Customer, 5, null, completedAt).Value;

        ReviewReport.Create(review, Customer, ReportReason.Spam, null, completedAt).Error.Should().Be(ReviewReportErrors.OwnReview);
        ReviewReport.Create(review, Guid.NewGuid(), ReportReason.Spam, "Advert", completedAt).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Completing_a_booking_raises_booking_completed()
    {
        var tour = TestData.Tour();
        var slot = TestData.Slot(tour);
        var booking = BookingService.BookSeats(tour, slot, Customer, 1, TestData.Now).Value;
        booking.Confirm(TestData.Now);
        booking.ClearDomainEvents();

        booking.Complete(slot.EndsAt);

        booking.DomainEvents.Should().Equal(new BookingCompleted(booking.Id));
    }

    private static (Booking Booking, DateTimeOffset CompletedAt) CompletedBooking()
    {
        var tour = TestData.Tour();
        var slot = TestData.Slot(tour);
        var booking = BookingService.BookSeats(tour, slot, Customer, 1, TestData.Now).Value;
        booking.Confirm(TestData.Now);
        booking.Complete(slot.EndsAt);
        booking.ClearDomainEvents();
        return (booking, slot.EndsAt);
    }
}
