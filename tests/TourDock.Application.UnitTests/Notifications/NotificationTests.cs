using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Notifications;
using TourDock.Domain.Agencies;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Slots;
using TourDock.Domain.Tours;

namespace TourDock.Application.UnitTests.Notifications;

public sealed class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly ITourRepository _tours = Substitute.For<ITourRepository>();
    private readonly IAgencyRepository _agencies = Substitute.For<IAgencyRepository>();
    private readonly IUserDirectory _users = Substitute.For<IUserDirectory>();
    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();
    private readonly ISentNotificationLog _sentLog = Substitute.For<ISentNotificationLog>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public void Maps_booking_events_to_notifications()
    {
        var mapper = new NotificationEventMapper();
        var id = Guid.NewGuid();

        mapper.Map(new BookingConfirmed(id)).Should().BeEquivalentTo(new { Kind = NotificationKind.BookingConfirmed, BookingId = id });
        mapper.Map(new BookingCancelled(id, CancelledBy.Customer, Money.Eur(40m))).Should()
            .BeEquivalentTo(new { Kind = NotificationKind.BookingCancelledByCustomer, Amount = 40m, Currency = "EUR" });
        mapper.Map(new BookingCancelled(id, CancelledBy.Admin, Money.Eur(40m))).Should()
            .BeEquivalentTo(new { Kind = NotificationKind.BookingCancelledByAgency });
        mapper.Map(new BookingGuestsReduced(id, 2, Money.Eur(100m))).Should()
            .BeEquivalentTo(new { Kind = NotificationKind.GuestsReduced, Guests = 2, Amount = 100m });
    }

    [Theory]
    [InlineData("Ana Marić", "Ana M.")]
    [InlineData("  ana   de la cruz ", "ana C.")]
    [InlineData("Ana", "Ana")]
    [InlineData("", "Traveler")]
    public void Author_names_are_shortened_for_privacy(string displayName, string shown)
    {
        Reviews.AuthorNames.Shorten(displayName).Should().Be(shown);
    }

    [Fact]
    public async Task Completed_trip_asks_only_the_traveler_for_a_review()
    {
        var (booking, _) = Arrange();
        var sent = CaptureSends();

        await Handler().HandleAsync(Message(NotificationKind.ReviewRequest, booking), Ct);

        sent.Should().ContainSingle().Which.Should().BeEquivalentTo(new { ToAddress = "ana@example.com", Subject = "How was Sunset Cruise?" });
        sent[0].TextBody.Should().Contain($"Write a review: https://tourdock.example.com/bookings?tab=past#booking-{booking.Id}");
    }

    [Fact]
    public async Task Confirmation_goes_to_the_traveler_and_the_agency()
    {
        var (booking, agency) = Arrange();
        var sent = CaptureSends();

        await Handler().HandleAsync(Message(NotificationKind.BookingConfirmed, booking), Ct);

        sent.Should().HaveCount(2);
        var traveler = sent.Single(m => m.ToAddress == "ana@example.com");
        traveler.Subject.Should().Be("Booking confirmed: Sunset Cruise on Sat 6 Jun");
        traveler.TextBody.Should().Contain("When: Saturday 6 June 2026, 20:00 (Europe/Zagreb)").And.Contain("Meeting point: Riva 1, Split");
        traveler.TextBody.Should().Contain($"View your booking: https://tourdock.example.com/bookings");
        var toAgency = sent.Single(m => m.ToAddress == agency.ContactEmail);
        toAgency.Subject.Should().StartWith("New booking:");
        toAgency.TextBody.Should().Contain("Ana booked and paid 2 guests").And.NotContain("ana@example.com", "the agency gets the name, not the address");
        _sentLog.Received(2).Record(Arg.Any<string>(), EmailDelivery.Sent);
    }

    [Fact]
    public async Task Agency_cancellation_only_emails_the_traveler_with_the_refund()
    {
        var (booking, _) = Arrange();
        var sent = CaptureSends();

        await Handler().HandleAsync(Message(NotificationKind.BookingCancelledByAgency, booking, amount: 100m), Ct);

        sent.Should().ContainSingle();
        sent[0].TextBody.Should().Contain("A refund of €100.00 is on its way");
    }

    [Fact]
    public async Task A_retry_skips_recipients_that_already_got_the_email()
    {
        var (booking, _) = Arrange();
        var message = Message(NotificationKind.BookingConfirmed, booking);
        _sentLog.WasSentAsync($"{message.NotificationId:N}.traveler", Arg.Any<CancellationToken>()).Returns(true);
        var sent = CaptureSends();

        await Handler().HandleAsync(message, Ct);

        sent.Should().ContainSingle().Which.ToAddress.Should().Be("bookings@bluebay.example.com");
    }

    [Fact]
    public async Task Html_part_encodes_user_supplied_text()
    {
        var (booking, _) = Arrange(title: "<script>alert(1)</script> Cruise");
        var sent = CaptureSends();

        await Handler().HandleAsync(Message(NotificationKind.BookingConfirmed, booking), Ct);

        sent[0].HtmlBody.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt; Cruise").And.NotContain("<script>");
    }

    [Fact]
    public async Task A_failing_send_throws_so_the_outbox_retries_and_nothing_is_recorded()
    {
        var (booking, _) = Arrange();
        _sender.SendAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs<Task<EmailDelivery>>(_ => throw new IOException("connection refused"));

        var act = () => Handler().HandleAsync(Message(NotificationKind.BookingConfirmed, booking), Ct);

        await act.Should().ThrowAsync<IOException>();
        _sentLog.DidNotReceiveWithAnyArgs().Record(default!, default);
    }

    private SendNotificationHandler Handler() =>
        new(_bookings, _tours, _agencies, Substitute.For<IReviewRepository>(), _users, _sender, _sentLog, _unitOfWork, new NotificationSettings("https://tourdock.example.com/"),
            new Observability.TourDockMetrics(new TestMeterFactory()), NullLogger<SendNotificationHandler>.Instance);

    private static SendNotificationMessage Message(NotificationKind kind, Booking booking, decimal? amount = null) =>
        new(Guid.NewGuid(), kind, booking.Id, amount, amount is null ? null : "EUR");

    private List<EmailMessage> CaptureSends()
    {
        var sent = new List<EmailMessage>();
        _sender.SendAsync(Arg.Do<EmailMessage>(sent.Add), Arg.Any<CancellationToken>()).Returns(EmailDelivery.Sent);
        return sent;
    }

    private (Booking Booking, Agency Agency) Arrange(string title = "Sunset Cruise")
    {
        var agency = Agency.Register("Blue Bay Tours", "bookings@bluebay.example.com", Guid.NewGuid()).Value;
        var location = Location.Create("HR", "Split", "Riva 1", 43.508, 16.440, "Europe/Zagreb").Value;
        var tour = Tour.Create(agency.Id, title, "Coast.", location, TimeSpan.FromHours(3), 10, BookingMode.Shared, Money.Eur(50m), null, null).Value;
        tour.Publish();
        var slot = Slot.Create(tour, Guid.NewGuid(), 12, new DateTimeOffset(2026, 6, 6, 18, 0, 0, TimeSpan.Zero), Now).Value;
        var customer = Guid.NewGuid();
        var booking = BookingService.BookSeats(tour, slot, customer, 2, Now).Value;
        booking.Confirm(Now);

        _bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        _tours.GetManyForDisplayAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(new Dictionary<Guid, Tour> { [tour.Id] = tour });
        _agencies.GetByIdAsync(booking.AgencyId, Arg.Any<CancellationToken>()).Returns(agency);
        _users.GetContactsAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(new Dictionary<Guid, UserContact> { [customer] = new("Ana", "ana@example.com") });
        return (booking, agency);
    }
}

internal sealed class TestMeterFactory : System.Diagnostics.Metrics.IMeterFactory
{
    private readonly List<System.Diagnostics.Metrics.Meter> _meters = [];

    public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options)
    {
        var meter = new System.Diagnostics.Metrics.Meter(options);
        _meters.Add(meter);
        return meter;
    }

    public void Dispose() => _meters.ForEach(m => m.Dispose());
}
