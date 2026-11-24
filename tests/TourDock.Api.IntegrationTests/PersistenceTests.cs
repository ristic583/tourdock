using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TourDock.Application.Abstractions.Data;
using TourDock.Domain.Agencies;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Slots;
using TourDock.Domain.Tours;
using TourDock.Domain.Vehicles;
using TourDock.Infrastructure.Identity;

namespace TourDock.Api.IntegrationTests;

/// <summary>Database-level guarantees that unit tests cannot prove: constraints, concurrency, mapping.</summary>
public sealed class PersistenceTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tour_with_money_location_policy_and_season_round_trips()
    {
        var (_, _, tour) = await SeedTourAsync();

        await using var db = factory.CreateDbContext();
        var loaded = await db.Tours.Include(t => t.SeasonalPrices).SingleAsync(t => t.Id == tour.Id, Ct);

        loaded.PricePerPerson.Should().Be(Money.Eur(49.99m));
        loaded.PrivatePrice.Should().Be(Money.Eur(450m));
        loaded.Location.TimeZoneId.Should().Be("Europe/Zagreb");
        loaded.CancellationPolicy.Should().Be(CancellationPolicy.Default);
        loaded.SeasonalPrices.Should().ContainSingle().Which.PricePerPerson.Should().Be(Money.Eur(69m));
        loaded.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Database_rejects_overlapping_departures_for_the_same_vehicle()
    {
        var (_, vehicle, tour) = await SeedTourAsync();
        var start = Now.AddDays(10);

        await using var db = factory.CreateDbContext();
        db.Slots.Add(Slot.Create(tour, vehicle.Id, vehicle.Capacity, start, Now).Value);
        db.Slots.Add(Slot.Create(tour, vehicle.Id, vehicle.Capacity, start.AddHours(1), Now).Value); // overlaps the 3h tour

        var save = () => db.SaveChangesAsync(Ct);

        (await save.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.ConstraintName.Should().Be("ex_slots_vehicle_no_overlap");
    }

    [Fact]
    public async Task Concurrent_reservations_on_the_same_departure_conflict_instead_of_overbooking()
    {
        var (_, vehicle, tour) = await SeedTourAsync();
        var slot = Slot.Create(tour, vehicle.Id, vehicle.Capacity, Now.AddDays(20), Now).Value;
        await using (var setup = factory.CreateDbContext())
        {
            setup.Slots.Add(slot);
            await setup.SaveChangesAsync(Ct);
        }

        // Two customers load the same departure at the same time...
        await using var first = factory.CreateDbContext();
        await using var second = factory.CreateDbContext();
        var firstTour = await first.Tours.Include(t => t.SeasonalPrices).SingleAsync(t => t.Id == tour.Id, Ct);
        var secondTour = await second.Tours.Include(t => t.SeasonalPrices).SingleAsync(t => t.Id == tour.Id, Ct);
        var firstSlot = await first.Slots.SingleAsync(s => s.Id == slot.Id, Ct);
        var secondSlot = await second.Slots.SingleAsync(s => s.Id == slot.Id, Ct);

        // ...both see enough free seats and book them.
        var (alice, bob) = (await CreateUserAsync(), await CreateUserAsync());
        first.Bookings.Add(BookingService.BookSeats(firstTour, firstSlot, alice, 4, Now).Value);
        second.Bookings.Add(BookingService.BookSeats(secondTour, secondSlot, bob, 4, Now).Value);

        await first.SaveChangesAsync(Ct);
        var secondSave = () => second.SaveChangesAsync(Ct);

        await secondSave.Should().ThrowAsync<ConcurrencyConflictException>();
        await using var verify = factory.CreateDbContext();
        (await verify.Slots.SingleAsync(s => s.Id == slot.Id, Ct)).ReservedSeats.Should().Be(4);
        (await verify.Bookings.CountAsync(b => b.SlotId == slot.Id, Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Soft_deleted_tours_are_hidden_from_queries()
    {
        var (_, _, tour) = await SeedTourAsync();
        await using (var db = factory.CreateDbContext())
        {
            var loaded = await db.Tours.SingleAsync(t => t.Id == tour.Id, Ct);
            loaded.Delete(Now);
            await db.SaveChangesAsync(Ct);
        }

        await using var verify = factory.CreateDbContext();
        (await verify.Tours.AnyAsync(t => t.Id == tour.Id, Ct)).Should().BeFalse();
        (await verify.Tours.IgnoreQueryFilters().AnyAsync(t => t.Id == tour.Id, Ct)).Should().BeTrue();
    }

    private async Task<Guid> CreateUserAsync()
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            DisplayName = "Test",
        };
        await using var db = factory.CreateDbContext();
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);
        return user.Id;
    }

    private async Task<(Agency Agency, Vehicle Vehicle, Tour Tour)> SeedTourAsync()
    {
        var agency = Agency.Register("Blue Coast Charters", "hello@example.com", await CreateUserAsync()).Value;
        agency.Approve();
        var vehicle = Vehicle.Create(agency.Id, "Sea Breeze", VehicleType.Boat, 8).Value;
        var location = Location.Create("HR", "Split", "Riva 1", 43.508, 16.440, "Europe/Zagreb").Value;
        var tour = Tour.Create(
            agency.Id, "Sunset Cruise", "Three hours along the coast.", location, TimeSpan.FromHours(3), 10,
            BookingMode.Both, Money.Eur(49.99m), Money.Eur(450m)).Value;
        tour.AddSeasonalPrice(new DateOnly(2027, 7, 1), new DateOnly(2027, 8, 31), Money.Eur(69m), null);
        tour.Publish();

        await using var db = factory.CreateDbContext();
        db.AddRange(agency, vehicle, tour);
        await db.SaveChangesAsync(Ct);
        return (agency, vehicle, tour);
    }
}
