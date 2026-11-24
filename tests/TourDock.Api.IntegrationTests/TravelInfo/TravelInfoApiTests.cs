using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using AwesomeAssertions;

namespace TourDock.Api.IntegrationTests.TravelInfo;

/// <summary>End to end through the API, with Frankfurter and Open-Meteo replaced by stubs.</summary>
public sealed class TravelInfoApiTests(TourDockApiFactory factory) : IClassFixture<TourDockApiFactory>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private TestApi Api() => new(factory.CreateClient(), Ct);

    [Fact]
    public async Task Currency_conversion_degrades_to_eur_only_and_never_changes_the_charged_price()
    {
        var api = Api();
        var departure = await api.CreateDepartureAsync();

        // Provider down and nothing cached yet: still 200, just no conversion.
        factory.ExchangeRatesHttp.ReturnStatus(HttpStatusCode.ServiceUnavailable);
        var (status, rates) = await api.SendAsync(HttpMethod.Get, "/api/v1/exchange-rates");
        status.Should().Be(HttpStatusCode.OK);
        rates.GetProperty("available").GetBoolean().Should().BeFalse();
        // (One failing call only: a second would open the circuit breaker for 30 s. The tour detail fallback is unit-tested.)

        // Provider back.
        factory.ExchangeRatesHttp.ReturnJson("""{"amount":1.0,"base":"EUR","date":"2026-11-06","rates":{"USD":1.0825,"GBP":0.8412}}""");
        (_, rates) = await api.SendAsync(HttpMethod.Get, "/api/v1/exchange-rates");
        rates.GetProperty("available").GetBoolean().Should().BeTrue();
        rates.GetProperty("baseCurrency").GetString().Should().Be("EUR");
        rates.GetProperty("rates").GetProperty("USD").GetDecimal().Should().Be(1.0825m);

        var (_, tour) = await api.SendAsync(HttpMethod.Get, $"/api/v1/tours/{departure.TourId}?currency=usd");
        tour.GetProperty("pricePerPerson").GetProperty("currency").GetString().Should().Be("EUR");
        var converted = tour.GetProperty("convertedPrices");
        converted.GetProperty("currency").GetString().Should().Be("USD");
        converted.GetProperty("pricePerPerson").GetProperty("amount").GetDecimal().Should().Be(54.13m);
        converted.GetProperty("privatePrice").GetProperty("amount").GetDecimal().Should().Be(433m);

        (status, _) = await api.SendAsync(HttpMethod.Get, $"/api/v1/tours/{departure.TourId}?currency=JPY");
        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Weather_is_shown_for_departures_within_the_forecast_horizon()
    {
        var api = Api();
        factory.WeatherHttp.Respond = (request, _) => Task.FromResult(FullDayForecast(request.RequestUri!));

        var soon = await api.CreateDepartureAsync(daysAhead: 3);
        var (status, weather) = await api.SendAsync(HttpMethod.Get, $"/api/v1/departures/{soon.DepartureId}/weather");
        status.Should().Be(HttpStatusCode.OK);
        weather.GetProperty("status").GetString().Should().Be("Available");
        var forecast = weather.GetProperty("forecast");
        forecast.GetProperty("summary").GetString().Should().Be("Partly cloudy");
        forecast.GetProperty("roughConditions").GetBoolean().Should().BeTrue("gusts of 50 km/h");
        forecast.GetProperty("time").GetDateTimeOffset().Should().Be(weather.GetProperty("startsAt").GetDateTimeOffset());

        var later = await api.CreateDepartureAsync(daysAhead: 30);
        (_, weather) = await api.SendAsync(HttpMethod.Get, $"/api/v1/departures/{later.DepartureId}/weather");
        weather.GetProperty("status").GetString().Should().Be("NotYetAvailable");
        weather.GetProperty("forecastAvailableFrom").ValueKind.Should().Be(JsonValueKind.String);

        (status, _) = await api.SendAsync(HttpMethod.Get, $"/api/v1/departures/{Guid.NewGuid()}/weather");
        status.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>24 hourly values for the requested UTC day, like Open-Meteo returns.</summary>
    private static HttpResponseMessage FullDayForecast(Uri request)
    {
        var date = HttpUtility.ParseQueryString(request.Query)["start_date"]!;
        var hours = Enumerable.Range(0, 24).ToList();
        string Repeat(string value) => string.Join(',', hours.Select(_ => value));
        var times = string.Join(',', hours.Select(h => $"\"{date}T{h.ToString("00", CultureInfo.InvariantCulture)}:00\""));
        var json = $$$"""
            {"hourly":{"time":[{{{times}}}],"temperature_2m":[{{{Repeat("21.5")}}}],"precipitation_probability":[{{{Repeat("20")}}}],
             "wind_speed_10m":[{{{Repeat("18.0")}}}],"wind_gusts_10m":[{{{Repeat("50.0")}}}],"weather_code":[{{{Repeat("2")}}}]}}
            """;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
