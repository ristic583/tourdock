using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using TourDock.Domain.Agencies;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;
using TourDock.Infrastructure.Identity;
using TourDock.Infrastructure.Persistence.Migrations;

namespace TourDock.Api.IntegrationTests;

public sealed class DemoMeetingPointMigrationTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Corrects_original_demo_locations_without_overwriting_custom_or_unrelated_tours()
    {
        await using var db = factory.CreateDbContext();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var owner = new ApplicationUser { Id = Guid.CreateVersion7(), Email = email, UserName = email, DisplayName = "Test" };
        var demo = Agency.Register("Dalmatian Jeep Safari", "guide.ana@example.com", owner.Id).Value;
        var unrelated = Agency.Register("Another agency", "another@example.com", owner.Id).Value;
        var original = Location.Create("ME", "Kotor", "Old town main gate", 42.4247, 18.7712, "Europe/Podgorica").Value;
        var custom = Location.Create("ME", "Kotor", "Our own meeting point", 42.425, 18.772, "Europe/Podgorica").Value;
        static Tour MakeTour(Agency agency, Location location) => Tour.Create(
            agency.Id, "Bay of Kotor Day Trip", "A day in the bay.", location, TimeSpan.FromHours(10), 20,
            BookingMode.Shared, Money.Eur(75), null).Value;
        var untouchedDemo = MakeTour(demo, original);
        var editedDemo = MakeTour(demo, custom);
        var unrelatedTour = MakeTour(unrelated, original);
        db.AddRange(owner, demo, unrelated, untouchedDemo, editedDemo, unrelatedTour);
        await db.SaveChangesAsync(Ct);

        var sql = new AlignDemoTourMeetingPoints().UpOperations.OfType<SqlOperation>().Single().Sql;
        await db.Database.ExecuteSqlRawAsync(sql, Ct);
        // Applying the correction twice must also be harmless.
        await db.Database.ExecuteSqlRawAsync(sql, Ct);
        db.ChangeTracker.Clear();

        var corrected = await db.Tours.SingleAsync(t => t.Id == untouchedDemo.Id, Ct);
        corrected.Location.Address.Should().Be("Sea Gate (main entrance to the old town)");
        corrected.Location.Latitude.Should().Be(42.424737);
        corrected.Location.Longitude.Should().Be(18.769705);
        (await db.Tours.SingleAsync(t => t.Id == editedDemo.Id, Ct)).Location.Should().Be(custom);
        (await db.Tours.SingleAsync(t => t.Id == unrelatedTour.Id, Ct)).Location.Should().Be(original);
    }
}
