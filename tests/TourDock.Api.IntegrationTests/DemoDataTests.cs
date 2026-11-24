using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;
using TourDock.Infrastructure.Seeding;

namespace TourDock.Api.IntegrationTests;

public sealed class DemoDataApiFactory : TourDockApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Seed:DemoData", "true");
    }
}

/// <summary>The demo seed: a multi-country catalog kept in sync on every start, with photos uploaded in the background.</summary>
public sealed class DemoDataTests(DemoDataApiFactory factory) : IClassFixture<DemoDataApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private DemoDataSeeder Seeder => factory.Services.GetServices<IHostedService>().OfType<DemoDataSeeder>().Single();

    [Fact]
    public async Task Demo_catalog_spans_many_countries_with_only_a_few_croatian_tours()
    {
        var api = new TestApi(factory.CreateClient(), Ct);

        var (status, page) = await api.SendAsync(HttpMethod.Get, "/api/v1/tours?pageSize=12", null);
        status.Should().Be(HttpStatusCode.OK);
        page.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(30);
        page.GetProperty("totalPages").GetInt32().Should().BeGreaterThanOrEqualTo(3);
        (await api.SendAsync(HttpMethod.Get, "/api/v1/tours?city=Belgrade", null)).Body.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(0);
        (await api.SendAsync(HttpMethod.Get, "/api/v1/tours?city=Lagos&radiusKm=50", null)).Body.GetProperty("totalCount").GetInt32()
            .Should().BeGreaterThanOrEqualTo(3, "the Benagil, kayak and dolphin tours are all within 50 km of Lagos");

        await using var db = factory.CreateDbContext();
        var published = await db.Tours.Where(t => t.Status == TourStatus.Published).Select(t => t.Location.CountryCode).ToListAsync(Ct);
        published.Distinct().Should().HaveCountGreaterThanOrEqualTo(10);
        published.Count(c => c == "HR").Should().BeLessThanOrEqualTo(3);
    }

    [Fact]
    public async Task A_restart_retires_dropped_tours_and_changes_nothing_else()
    {
        await Seeder.PhotoSeeding.WaitAsync(TimeSpan.FromMinutes(3), Ct); // don't race the background upload

        await using (var db = factory.CreateDbContext())
        {
            // A tour an older version of the demo seeded and the catalog has since dropped.
            var hvar = await db.Agencies.SingleAsync(a => a.ContactEmail == "skipper.luka@example.com", Ct);
            var boat = await db.Vehicles.FirstAsync(v => v.AgencyId == hvar.Id, Ct);
            var location = Location.Create("HR", "Hvar", "Riva 21, Hvar town harbour", 43.171182, 16.441340, "Europe/Zagreb").Value;
            var old = Tour.Create(hvar.Id, "Morning Fishing Trip", "Old demo tour.", location, TimeSpan.FromHours(4), 6, BookingMode.Shared,
                Money.Eur(70m), null).Value;
            old.AddScheduleRule(boat.Id, DaysOfWeek.Monday, new TimeOnly(6, 0), new DateOnly(2026, 1, 1), null);
            old.Publish();
            db.Tours.Add(old);
            await db.SaveChangesAsync(Ct);
        }

        DateTimeOffset[] before;
        int tours;
        await using (var db = factory.CreateDbContext())
        {
            tours = await db.Tours.CountAsync(t => !t.IsDeleted, Ct);
            before = await db.Tours.Where(t => !t.IsDeleted && t.Title != "Morning Fishing Trip").OrderBy(t => t.Id).Select(t => t.UpdatedAt).ToArrayAsync(Ct);
        }

        await Seeder.StartAsync(Ct); // a restart, or the next deploy

        await using (var db = factory.CreateDbContext())
        {
            var retired = await db.Tours.IgnoreQueryFilters().SingleAsync(t => t.Title == "Morning Fishing Trip", Ct);
            retired.IsDeleted.Should().BeTrue();
            (await db.Tours.CountAsync(t => !t.IsDeleted, Ct)).Should().Be(tours - 1, "nothing is duplicated");
            var after = await db.Tours.Where(t => !t.IsDeleted).OrderBy(t => t.Id).Select(t => t.UpdatedAt).ToArrayAsync(Ct);
            after.Should().Equal(before, "tours already in their catalog state are not rewritten");
        }
    }

    [Fact]
    public async Task Demo_tours_get_their_photos_in_the_background()
    {
        await Seeder.PhotoSeeding.WaitAsync(TimeSpan.FromMinutes(3), Ct);

        var api = new TestApi(factory.CreateClient(), Ct);
        var (_, page) = await api.SendAsync(HttpMethod.Get, "/api/v1/tours?pageSize=50", null);
        var items = page.GetProperty("items").EnumerateArray().ToList();
        items.Should().NotBeEmpty();
        items.Should().AllSatisfy(t => t.GetProperty("coverPhotoUrl").GetString().Should().NotBeNullOrEmpty());

        await using var db = factory.CreateDbContext();
        (await db.Tours.Where(t => !t.IsDeleted).AllAsync(t => t.Photos.Count >= 2, Ct)).Should().BeTrue();
    }
}
