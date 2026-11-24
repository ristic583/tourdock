using AwesomeAssertions;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Domain.UnitTests;

public sealed class TourTests
{
    [Fact]
    public void Shared_tour_requires_price_per_person()
    {
        var result = Tour.Create(Guid.NewGuid(), "Trip", "", TestData.Split, TimeSpan.FromHours(2), 8, BookingMode.Shared, null, null);

        result.Error!.Code.Should().Be("Tour.MissingPrice");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(501)]
    public void Rejects_invalid_max_guests(int maxGuests)
    {
        var result = Tour.Create(
            Guid.NewGuid(), "Trip", "", TestData.Split, TimeSpan.FromHours(2), maxGuests, BookingMode.Shared, Money.Eur(10), null);

        result.Error!.Code.Should().Be("Tour.InvalidMaxGuests");
    }

    [Fact]
    public void Seasonal_price_overrides_base_price_inside_the_season()
    {
        var tour = TestData.Tour(perPerson: 50m);
        tour.AddSeasonalPrice(new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 31), Money.Eur(70m), null);

        tour.PricingOn(new DateOnly(2026, 7, 15)).PricePerPerson!.Amount.Should().Be(70m);
        tour.PricingOn(new DateOnly(2026, 9, 1)).PricePerPerson!.Amount.Should().Be(50m);
        tour.PricingOn(new DateOnly(2026, 7, 15)).PrivatePrice!.Amount.Should().Be(400m, "the season does not override the private price");
    }

    [Fact]
    public void Seasons_cannot_overlap()
    {
        var tour = TestData.Tour();
        tour.AddSeasonalPrice(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), Money.Eur(70m), Money.Eur(500m));

        var result = tour.AddSeasonalPrice(new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 31), Money.Eur(80m), Money.Eur(600m));

        result.Error.Should().Be(TourErrors.OverlappingSeason);
    }

    [Fact]
    public void Unknown_time_zone_is_rejected()
    {
        var result = Location.Create("HR", "Split", "", 43.5, 16.4, "Mars/Olympus");

        result.Error!.Code.Should().Be("Tour.InvalidLocation");
    }

    [Theory]
    [InlineData(100, 100)] // 100h before: first tier
    [InlineData(72, 100)]  // exactly on the boundary
    [InlineData(48, 50)]
    [InlineData(23, 0)]
    public void Cancellation_policy_picks_the_matching_tier(int hoursBefore, int expectedPercent) =>
        CancellationPolicy.Default.RefundPercentFor(TimeSpan.FromHours(hoursBefore)).Should().Be(expectedPercent);

    [Fact]
    public void Cancellation_policy_rejects_refunds_that_grow_closer_to_departure()
    {
        var result = CancellationPolicy.Create([new RefundTier(72, 50), new RefundTier(24, 80)]);

        result.IsFailure.Should().BeTrue();
    }
}

public sealed class TourPhotoTests
{
    [Fact]
    public void First_photo_becomes_cover_and_removing_the_cover_promotes_the_next()
    {
        var tour = TestData.Tour();
        var first = tour.AddPhoto(Guid.NewGuid(), "a", 2000, 1333).Value;
        var second = tour.AddPhoto(Guid.NewGuid(), "b", 2000, 1333).Value;

        tour.CoverPhoto.Should().Be(first);
        tour.RemovePhoto(first.Id);

        tour.CoverPhoto.Should().Be(second);
    }

    [Fact]
    public void A_tour_has_at_most_twenty_photos()
    {
        var tour = TestData.Tour();
        for (var i = 0; i < Tour.MaxPhotos; i++)
        {
            tour.AddPhoto(Guid.NewGuid(), $"k{i}", 100, 100);
        }

        tour.AddPhoto(Guid.NewGuid(), "extra", 100, 100).Error.Should().Be(TourErrors.TooManyPhotos);
    }

    [Fact]
    public void Reorder_requires_every_photo_exactly_once()
    {
        var tour = TestData.Tour();
        var a = tour.AddPhoto(Guid.NewGuid(), "a", 100, 100).Value.Id;
        var b = tour.AddPhoto(Guid.NewGuid(), "b", 100, 100).Value.Id;

        tour.ReorderPhotos([a]).Error.Should().Be(TourErrors.InvalidPhotoOrder);
        tour.ReorderPhotos([a, a]).Error.Should().Be(TourErrors.InvalidPhotoOrder);
        tour.ReorderPhotos([b, a]).IsSuccess.Should().BeTrue();
        tour.Photos.Select(p => p.Id).Should().Equal(b, a);
    }
}

public sealed class TourRuleTests
{
    private static readonly Guid Boat = Guid.NewGuid();
    private static readonly DateOnly June = new(2026, 6, 1);

    [Fact]
    public void The_whole_trip_cannot_cost_less_than_one_seat()
    {
        var tour = TestData.Tour(perPerson: 50m);

        tour.UpdatePricing(BookingMode.Both, Money.Eur(50m), Money.Eur(40m)).Error!.Code.Should().Be("Tour.PrivatePriceTooLow");
        tour.UpdatePricing(BookingMode.Both, Money.Eur(50m), Money.Eur(50m)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void A_schedule_that_repeats_an_active_one_is_rejected()
    {
        var tour = TestData.Tour();
        tour.AddScheduleRule(Boat, DaysOfWeek.Monday | DaysOfWeek.Wednesday, new TimeOnly(9, 0), June, null).IsSuccess.Should().BeTrue();

        tour.AddScheduleRule(Boat, DaysOfWeek.Wednesday, new TimeOnly(9, 0), June.AddDays(10), null).Error!.Code
            .Should().Be("Tour.DuplicateScheduleRule");
    }

    [Fact]
    public void Schedules_that_differ_in_time_vehicle_days_or_dates_are_fine()
    {
        var tour = TestData.Tour();
        tour.AddScheduleRule(Boat, DaysOfWeek.Monday, new TimeOnly(9, 0), June, June.AddDays(30)).IsSuccess.Should().BeTrue();

        tour.AddScheduleRule(Boat, DaysOfWeek.Monday, new TimeOnly(15, 0), June, null).IsSuccess.Should().BeTrue();
        tour.AddScheduleRule(Guid.NewGuid(), DaysOfWeek.Monday, new TimeOnly(9, 0), June, null).IsSuccess.Should().BeTrue();
        tour.AddScheduleRule(Boat, DaysOfWeek.Tuesday, new TimeOnly(9, 0), June, null).IsSuccess.Should().BeTrue();
        tour.AddScheduleRule(Boat, DaysOfWeek.Monday, new TimeOnly(9, 0), June.AddDays(31), null).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void A_stopped_schedule_can_be_added_again()
    {
        var tour = TestData.Tour();
        var first = tour.AddScheduleRule(Boat, DaysOfWeek.Friday, new TimeOnly(9, 0), June, null).Value;
        tour.DeactivateScheduleRule(first.Id);

        tour.AddScheduleRule(Boat, DaysOfWeek.Friday, new TimeOnly(9, 0), June, null).IsSuccess.Should().BeTrue();
    }
}
