using AwesomeAssertions;
using NSubstitute;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.TravelInfo;
using TourDock.Domain.Common;
using TourDock.Domain.Slots;
using TourDock.Domain.Tours;

namespace TourDock.Application.UnitTests.TravelInfo;

public sealed class DepartureWeatherTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly ISlotRepository _slots = Substitute.For<ISlotRepository>();
    private readonly ITourRepository _tours = Substitute.For<ITourRepository>();
    private readonly IWeatherService _weather = Substitute.For<IWeatherService>();

    [Fact]
    public async Task Returns_the_forecast_for_the_departure_hour_at_the_tour_location()
    {
        var slot = Arrange(startsIn: TimeSpan.FromDays(3));
        var forecast = new HourlyWeather(slot.StartsAt, 24.5, 10, 12, 20, 1);
        _weather.GetHourlyAsync(43.508, 16.440, slot.StartsAt, Arg.Any<CancellationToken>()).Returns(forecast);

        var result = await Handle(slot, Now);

        result.Value.Status.Should().Be(WeatherForecastStatus.Available);
        result.Value.Forecast.Should().BeEquivalentTo(new { TemperatureC = 24.5, Summary = "Mainly clear", RoughConditions = false });
    }

    [Fact]
    public async Task Does_not_ask_for_a_forecast_beyond_the_horizon()
    {
        var slot = Arrange(startsIn: TimeSpan.FromDays(20));

        var result = await Handle(slot, Now);

        result.Value.Status.Should().Be(WeatherForecastStatus.NotYetAvailable);
        result.Value.ForecastAvailableFrom.Should().Be(slot.StartsAt - TimeSpan.FromDays(14));
        await _weather.DidNotReceiveWithAnyArgs().GetHourlyAsync(default, default, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_past_departures()
    {
        var slot = Arrange(startsIn: TimeSpan.FromDays(1));

        var result = await Handle(slot, Now.AddDays(2));

        result.Value.Status.Should().Be(WeatherForecastStatus.Past);
    }

    [Fact]
    public async Task Degrades_to_unavailable_when_the_provider_has_nothing()
    {
        var slot = Arrange(startsIn: TimeSpan.FromDays(3));
        _weather.GetHourlyAsync(default, default, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs((HourlyWeather?)null);

        var result = await Handle(slot, Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(WeatherForecastStatus.Unavailable);
        result.Value.Forecast.Should().BeNull();
    }

    [Fact]
    public async Task Hides_departures_of_tours_that_are_not_bookable()
    {
        var slot = Arrange(startsIn: TimeSpan.FromDays(3), publish: false);

        var result = await Handle(slot, Now);

        result.Error.Should().Be(TravelInfoErrors.DepartureNotFound);
    }

    [Theory]
    [InlineData(10, 20, 2, false)]
    [InlineData(30, 35, 2, true)]  // sustained wind
    [InlineData(15, 45, 2, true)]  // gusts
    [InlineData(5, 10, 95, true)]  // thunderstorm
    public void Flags_rough_conditions(double wind, double gusts, int code, bool rough)
    {
        WeatherCodes.IsRough(new HourlyWeather(Now, 20, 0, wind, gusts, code)).Should().Be(rough);
    }

    private async Task<Domain.Common.Result<DepartureWeatherResponse>> Handle(Slot slot, DateTimeOffset now) =>
        await new GetDepartureWeatherHandler(_slots, _tours, _weather, new FixedTime(now))
            .Handle(new GetDepartureWeatherQuery(slot.Id), TestContext.Current.CancellationToken);

    private Slot Arrange(TimeSpan startsIn, bool publish = true)
    {
        var location = Location.Create("HR", "Split", "Riva 1", 43.508, 16.440, "Europe/Zagreb").Value;
        var tour = Tour.Create(Guid.NewGuid(), "Sunset Cruise", "Three hours along the coast.", location, TimeSpan.FromHours(3), 10,
            BookingMode.Shared, Money.Eur(50m), null, null).Value;
        if (publish)
        {
            tour.Publish();
        }

        var slot = Slot.Create(tour, Guid.NewGuid(), 12, Now + startsIn, Now).Value;
        _slots.GetByIdAsync(slot.Id, Arg.Any<CancellationToken>()).Returns(slot);
        _tours.GetByIdAsync(tour.Id, Arg.Any<CancellationToken>()).Returns(tour);
        return slot;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
