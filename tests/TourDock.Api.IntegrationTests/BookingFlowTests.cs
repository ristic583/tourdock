using System.Net;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Bookings;
using TourDock.Domain.Bookings;

namespace TourDock.Api.IntegrationTests;

public sealed class BookingFlowTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private TestApi Api() => new(factory.CreateClient(), Ct);

    [Fact]
    public async Task Booking_seats_holds_them_for_fifteen_minutes()
    {
        var api = Api();
        var departure = await api.CreateDepartureAsync(capacity: 8);
        var traveler = await api.RegisterUserAsync();

        var (status, booking) = await api.BookAsync(traveler, departure.DepartureId, guests: 3);

        status.Should().Be(HttpStatusCode.Created);
        booking.GetProperty("status").GetString().Should().Be("Pending");
        booking.GetProperty("totalPrice").GetProperty("amount").GetDecimal().Should().Be(150m);
        booking.GetProperty("holdExpiresAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));
        booking.GetProperty("localStart").GetString().Should().EndWith("T09:00:00");
        (await api.RemainingSeatsAsync(departure)).Should().Be(5);
    }

    [Fact]
    public async Task Idempotency_key_is_required()
    {
        var api = Api();
        var departure = await api.CreateDepartureAsync();
        var traveler = await api.RegisterUserAsync();

        var (status, body) = await api.SendAsync(HttpMethod.Post, "/api/v1/bookings",
            new { departureId = departure.DepartureId, type = "Shared", guests = 1 }, traveler);

        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("errors").TryGetProperty("idempotencyKey", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Retrying_with_the_same_key_returns_the_same_booking_and_reserves_once()
    {
        var api = Api();
        var departure = await api.CreateDepartureAsync(capacity: 8);
        var traveler = await api.RegisterUserAsync();
        var key = Guid.NewGuid().ToString();

        var first = await api.BookAsync(traveler, departure.DepartureId, 2, key: key);
        var retry = await api.BookAsync(traveler, departure.DepartureId, 2, key: key);

        first.Status.Should().Be(HttpStatusCode.Created);
        retry.Status.Should().Be(HttpStatusCode.OK);
        retry.Body.GetProperty("id").GetString().Should().Be(first.Body.GetProperty("id").GetString());
        (await api.RemainingSeatsAsync(departure)).Should().Be(6);

        // Same key, different request: rejected rather than silently returning the wrong booking.
        var misuse = await api.BookAsync(traveler, departure.DepartureId, 5, key: key);
        misuse.Status.Should().Be(HttpStatusCode.Conflict);
        misuse.Body.GetProperty("code").GetString().Should().Be("Booking.IdempotencyKeyReused");
    }

    [Fact]
    public async Task Double_click_sends_the_same_request_twice_at_once_and_creates_one_booking()
    {
        var api = Api();
        var departure = await api.CreateDepartureAsync(capacity: 8);
        var traveler = await api.RegisterUserAsync();
        var key = Guid.NewGuid().ToString();

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => api.BookAsync(traveler, departure.DepartureId, 2, key: key)));

        results.Should().OnlyContain(r => r.Status == HttpStatusCode.Created || r.Status == HttpStatusCode.OK);
        results.Select(r => r.Body.GetProperty("id").GetString()).Distinct().Should().ContainSingle();
        (await api.RemainingSeatsAsync(departure)).Should().Be(6);
    }

    [Fact]
    public async Task Six_travelers_racing_for_four_seats_never_overbook()
    {
        var api = Api();
        var departure = await api.CreateDepartureAsync(capacity: 4);
        var travelers = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => api.RegisterUserAsync()));

        var results = await Task.WhenAll(travelers.Select(t => api.BookAsync(t, departure.DepartureId, guests: 1)));

        results.Count(r => r.Status == HttpStatusCode.Created).Should().Be(4);
        results.Where(r => r.Status != HttpStatusCode.Created).Should().OnlyContain(r => r.Status == HttpStatusCode.Conflict);
        (await api.RemainingSeatsAsync(departure)).Should().Be(0);
    }

    [Fact]
    public async Task Private_booking_takes_the_whole_departure()
    {
        var api = Api();
        var departure = await api.CreateDepartureAsync(capacity: 8);

        var privateBooking = await api.BookAsync(await api.RegisterUserAsync(), departure.DepartureId, 4, type: "Private");
        var seat = await api.BookAsync(await api.RegisterUserAsync(), departure.DepartureId, 1);

        privateBooking.Status.Should().Be(HttpStatusCode.Created);
        privateBooking.Body.GetProperty("totalPrice").GetProperty("amount").GetDecimal().Should().Be(400m);
        seat.Status.Should().Be(HttpStatusCode.Conflict);
        seat.Body.GetProperty("code").GetString().Should().Be("Slot.NotEnoughSeats");
    }

    [Fact]
    public async Task Customer_cancellation_refunds_by_policy_and_frees_seats()
    {
        var api = Api();
        var departure = await api.CreateDepartureAsync(capacity: 8, daysAhead: 10); // > 72h before: 100% refund
        var traveler = await api.RegisterUserAsync();
        var bookingId = (await api.BookAsync(traveler, departure.DepartureId, 2)).Body.GetProperty("id").GetString()!;
        await ConfirmAsync(bookingId);

        var (status, result) = await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/cancel", new { reason = "Plans changed" }, traveler);

        status.Should().Be(HttpStatusCode.OK);
        result.GetProperty("refund").GetProperty("amount").GetDecimal().Should().Be(100m);
        result.GetProperty("booking").GetProperty("status").GetString().Should().Be("Cancelled");
        (await api.RemainingSeatsAsync(departure)).Should().Be(8);
    }

    [Fact]
    public async Task Reducing_guests_refunds_removed_seats()
    {
        var api = Api();
        var departure = await api.CreateDepartureAsync(capacity: 8);
        var traveler = await api.RegisterUserAsync();
        var bookingId = (await api.BookAsync(traveler, departure.DepartureId, 5)).Body.GetProperty("id").GetString()!;
        await ConfirmAsync(bookingId);

        var (status, result) = await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/reduce-guests", new { guests = 3 }, traveler);

        status.Should().Be(HttpStatusCode.OK);
        result.GetProperty("refund").GetProperty("amount").GetDecimal().Should().Be(100m);
        result.GetProperty("booking").GetProperty("guests").GetInt32().Should().Be(3);
        (await api.RemainingSeatsAsync(departure)).Should().Be(5);
    }

    [Fact]
    public async Task Bookings_are_private_to_their_owner_and_the_agency()
    {
        var api = Api();
        var departure = await api.CreateDepartureAsync();
        var traveler = await api.RegisterUserAsync();
        var bookingId = (await api.BookAsync(traveler, departure.DepartureId, 1)).Body.GetProperty("id").GetString()!;
        var stranger = await api.RegisterUserAsync();

        (await api.SendAsync(HttpMethod.Get, $"/api/v1/bookings/{bookingId}", null, stranger)).Status.Should().Be(HttpStatusCode.NotFound);
        (await api.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/cancel", new { }, stranger)).Status.Should().Be(HttpStatusCode.NotFound);

        var (_, mine) = await api.SendAsync(HttpMethod.Get, "/api/v1/me/bookings", null, traveler);
        mine.EnumerateArray().Select(b => b.GetProperty("id").GetString()).Should().Contain(bookingId);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (_, agencyList) = await api.SendAsync(HttpMethod.Get,
            $"/api/v1/agencies/{departure.AgencyId}/bookings?from={today:yyyy-MM-dd}&to={today.AddDays(30):yyyy-MM-dd}", null, departure.AgencyToken);
        var row = agencyList.EnumerateArray().Single(b => b.GetProperty("booking").GetProperty("id").GetString() == bookingId);
        row.GetProperty("customerName").GetString().Should().Be("Test User");
    }

    [Fact]
    public async Task Agency_cancellation_refunds_everything_paid()
    {
        var api = Api();
        var departure = await api.CreateDepartureAsync(daysAhead: 2); // inside the 50% window for customers
        var traveler = await api.RegisterUserAsync();
        var bookingId = (await api.BookAsync(traveler, departure.DepartureId, 2)).Body.GetProperty("id").GetString()!;
        await ConfirmAsync(bookingId);

        var (status, result) = await api.SendAsync(HttpMethod.Post,
            $"/api/v1/agencies/{departure.AgencyId}/bookings/{bookingId}/cancel", new { reason = "Engine failure" }, departure.AgencyToken);

        status.Should().Be(HttpStatusCode.OK);
        result.GetProperty("refund").GetProperty("amount").GetDecimal().Should().Be(100m);
        result.GetProperty("booking").GetProperty("cancelledBy").GetString().Should().Be("Agency");
    }

    [Fact]
    public async Task Expired_holds_release_their_seats()
    {
        var api = Api();
        var departure = await api.CreateDepartureAsync(capacity: 4);
        var bookingId = Guid.Parse((await api.BookAsync(await api.RegisterUserAsync(), departure.DepartureId, 3)).Body.GetProperty("id").GetString()!);

        // Simulate 15 minutes passing.
        await using (var db = factory.CreateDbContext())
        {
            await db.Bookings.Where(b => b.Id == bookingId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.HoldExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var expire = scope.ServiceProvider.GetRequiredService<ICommandHandler<ExpireHoldsCommand, int>>();
        (await expire.Handle(new ExpireHoldsCommand(), Ct)).Value.Should().BeGreaterThanOrEqualTo(1);

        await using var verify = factory.CreateDbContext();
        (await verify.Bookings.SingleAsync(b => b.Id == bookingId, Ct)).Status.Should().Be(BookingStatus.Expired);
        (await api.RemainingSeatsAsync(departure)).Should().Be(4);
    }

    [Fact]
    public async Task One_booking_changed_meanwhile_does_not_stop_the_others_from_completing()
    {
        var api = Api();
        var departure = await api.CreateDepartureAsync(capacity: 4);
        var ids = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            var id = (await api.BookAsync(await api.RegisterUserAsync(), departure.DepartureId, 1)).Body.GetProperty("id").GetString()!;
            await ConfirmAsync(id);
            ids.Add(Guid.Parse(id));
        }

        var (busy, other) = (ids[0], ids[1]);
        var trigger = $"skip_{busy:N}";
        await using (var db = factory.CreateDbContext())
        {
            await db.Bookings.Where(b => ids.Contains(b.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.SlotEndsAt, DateTimeOffset.UtcNow.AddHours(-1)), Ct);

            // The update of one booking silently affects no row, exactly what EF sees when another request changed it first.
            var sql = $"""
                CREATE FUNCTION {trigger}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN NULL; END $$;
                CREATE TRIGGER {trigger} BEFORE UPDATE ON bookings FOR EACH ROW WHEN (OLD.id = '{busy}') EXECUTE FUNCTION {trigger}();
                """;
            await db.Database.ExecuteSqlRawAsync(sql, Ct);
        }

        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var complete = scope.ServiceProvider.GetRequiredService<ICommandHandler<CompleteFinishedBookingsCommand, int>>();
            (await complete.Handle(new CompleteFinishedBookingsCommand(), Ct)).IsSuccess.Should().BeTrue();

            await using var verify = factory.CreateDbContext();
            (await verify.Bookings.SingleAsync(b => b.Id == other, Ct)).Status.Should().Be(BookingStatus.Completed);
            (await verify.Bookings.SingleAsync(b => b.Id == busy, Ct)).Status.Should().Be(BookingStatus.Confirmed, "retried on the next run");
        }
        finally
        {
            await using var db = factory.CreateDbContext();
            var drop = $"DROP TRIGGER {trigger} ON bookings; DROP FUNCTION {trigger}();";
            await db.Database.ExecuteSqlRawAsync(drop, Ct);
        }
    }

    /// <summary>Payments arrive in the next segment; until then tests confirm bookings directly.</summary>
    private async Task ConfirmAsync(string bookingId)
    {
        await using var db = factory.CreateDbContext();
        var booking = await db.Bookings.SingleAsync(b => b.Id == Guid.Parse(bookingId), Ct);
        booking.Confirm(DateTimeOffset.UtcNow).IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync(Ct);
    }
}
