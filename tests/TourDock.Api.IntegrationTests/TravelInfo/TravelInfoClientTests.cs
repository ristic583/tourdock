using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TourDock.Application.Abstractions;
using TourDock.Infrastructure.TravelInfo;

namespace TourDock.Api.IntegrationTests.TravelInfo;

/// <summary>
/// The real exchange rate and weather clients (HTTP pipeline, resilience, cache) against a stubbed network.
/// No containers: each test builds its own container of services, so cache and circuit state never leak between tests.
/// </summary>
public sealed class TravelInfoClientTests : IDisposable
{
    private const string RatesJson = """{"amount":1.0,"base":"EUR","date":"2026-11-06","rates":{"USD":1.0825,"GBP":0.8412,"HUF":391.47}}""";

    private const string ForecastJson = """
        {"latitude":43.5,"longitude":16.44,"hourly":{
          "time":["2026-06-04T08:00","2026-06-04T09:00","2026-06-04T10:00"],
          "temperature_2m":[22.1,23.4,null],
          "precipitation_probability":[5,10,15],
          "wind_speed_10m":[8.2,11.5,14.0],
          "wind_gusts_10m":[15.1,null,25.0],
          "weather_code":[0,2,3]}}
        """;

    private readonly StubHttpHandler _ratesHttp = new();
    private readonly StubHttpHandler _weatherHttp = new();
    private ServiceProvider? _services;

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Fetches_eur_rates_once_and_serves_them_from_cache()
    {
        _ratesHttp.ReturnJson(RatesJson);
        var rates = Rates();

        var first = await rates.GetLatestAsync(Ct);
        var second = await rates.GetLatestAsync(Ct);

        first.Should().BeEquivalentTo(new { BaseCurrency = "EUR", Date = new DateOnly(2026, 11, 6), IsStale = false });
        first!.Rates["USD"].Should().Be(1.0825m);
        second.Should().BeEquivalentTo(first);
        _ratesHttp.Calls.Should().Be(1);
        _ratesHttp.Requests[0].Query.Should().Contain("base=EUR").And.Contain("symbols=USD,GBP");
    }

    [Fact]
    public async Task Retries_a_transient_failure()
    {
        var attempt = 0;
        _ratesHttp.Respond = (_, _) => Task.FromResult(Interlocked.Increment(ref attempt) == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Json(RatesJson));

        var result = await Rates().GetLatestAsync(Ct);

        result.Should().NotBeNull();
        _ratesHttp.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Serves_the_last_good_rates_marked_stale_while_the_provider_is_down()
    {
        _ratesHttp.ReturnJson(RatesJson);
        var rates = Rates(new() { ["ExchangeRates:CacheDuration"] = "00:00:01" });
        await rates.GetLatestAsync(Ct);

        _ratesHttp.ReturnStatus(HttpStatusCode.InternalServerError);
        await Task.Delay(TimeSpan.FromMilliseconds(1200), Ct); // let the fresh entry expire

        var result = await rates.GetLatestAsync(Ct);

        result.Should().BeEquivalentTo(new { IsStale = true, Date = new DateOnly(2026, 11, 6) });
        result!.Rates["GBP"].Should().Be(0.8412m);
    }

    [Fact]
    public async Task Returns_nothing_instead_of_throwing_when_no_rates_were_ever_fetched()
    {
        _ratesHttp.ReturnStatus(HttpStatusCode.ServiceUnavailable);

        var result = await Rates().GetLatestAsync(Ct);

        result.Should().BeNull();
        _ratesHttp.Calls.Should().Be(3, "one attempt and two retries");
    }

    [Theory]
    [InlineData("""{"base":"USD","date":"2026-11-06","rates":{"EUR":0.92}}""")]
    [InlineData("""{"base":"EUR","date":"2026-11-06","rates":{"USD":0}}""")]
    [InlineData("""{"base":"EUR","date":"2026-11-06","rates":{}}""")]
    [InlineData("""<html>maintenance</html>""")]
    public async Task Rejects_implausible_responses(string body)
    {
        _ratesHttp.ReturnJson(body);

        (await Rates().GetLatestAsync(Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Opens_the_circuit_after_repeated_failures_and_stops_calling_the_provider()
    {
        _ratesHttp.ReturnStatus(HttpStatusCode.ServiceUnavailable);
        var rates = Rates();
        await rates.GetLatestAsync(Ct);
        await rates.GetLatestAsync(Ct); // 6 failed attempts: above the minimum throughput, 100 % failure ratio
        var callsWhenOpen = _ratesHttp.Calls;

        var result = await rates.GetLatestAsync(Ct);

        result.Should().BeNull();
        _ratesHttp.Calls.Should().Be(callsWhenOpen, "an open circuit fails fast without touching the network");
    }

    [Fact]
    public async Task Picks_the_forecast_hour_nearest_to_the_departure()
    {
        _weatherHttp.ReturnJson(ForecastJson);

        var result = await Weather().GetHourlyAsync(43.508, 16.4404, new DateTimeOffset(2026, 6, 4, 11, 20, 0, TimeSpan.FromHours(2)), Ct);

        result.Should().Be(new HourlyWeather(new DateTimeOffset(2026, 6, 4, 9, 0, 0, TimeSpan.Zero), 23.4, 10, 11.5, 11.5, 2));
        _weatherHttp.Requests[0].Query.Should()
            .Contain("latitude=43.51&longitude=16.44")
            .And.Contain("start_date=2026-06-04&end_date=2026-06-04")
            .And.Contain("timezone=GMT");
    }

    [Fact]
    public async Task Skips_hours_without_data_and_shares_one_call_per_place_and_day()
    {
        _weatherHttp.ReturnJson(ForecastJson);
        var weather = Weather();

        var missing = await weather.GetHourlyAsync(43.508, 16.44, new DateTimeOffset(2026, 6, 4, 9, 45, 0, TimeSpan.Zero), Ct);
        var morning = await weather.GetHourlyAsync(43.509, 16.44, new DateTimeOffset(2026, 6, 4, 8, 0, 0, TimeSpan.Zero), Ct);

        missing.Should().BeNull("10:00 has no temperature");
        morning!.TemperatureC.Should().Be(22.1);
        _weatherHttp.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Weather_degrades_to_nothing_when_the_provider_is_down()
    {
        _weatherHttp.ReturnStatus(HttpStatusCode.BadGateway);

        var result = await Weather().GetHourlyAsync(43.5, 16.44, DateTimeOffset.UtcNow.AddDays(2), Ct);

        result.Should().BeNull();
    }

    public void Dispose() => _services?.Dispose();

    private IExchangeRateService Rates(Dictionary<string, string?>? settings = null) => Services(settings).GetRequiredService<IExchangeRateService>();

    private IWeatherService Weather() => Services().GetRequiredService<IWeatherService>();

    private ServiceProvider Services(Dictionary<string, string?>? settings = null)
    {
        if (_services is not null)
        {
            return _services;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddTravelInfo(configuration);
        services.AddHttpClient(TravelInfoHttpClients.ExchangeRates).ConfigurePrimaryHttpMessageHandler(() => _ratesHttp);
        services.AddHttpClient(TravelInfoHttpClients.Weather).ConfigurePrimaryHttpMessageHandler(() => _weatherHttp);
        return _services = services.BuildServiceProvider();
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
