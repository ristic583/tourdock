using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TourDock.Application.Abstractions;

namespace TourDock.Infrastructure.TravelInfo;

/// <summary>
/// Hourly forecasts from Open-Meteo. One call fetches a whole UTC day for a place; the day is cached so every
/// departure of that tour on that day shares it. Coordinates are rounded to 2 decimals (about 1 km), which is
/// finer than the forecast grid, so nearby tours share cache entries too.
/// </summary>
internal sealed partial class OpenMeteoWeatherService(
    IHttpClientFactory httpClients,
    HybridCache cache,
    IOptions<WeatherOptions> options,
    ILogger<OpenMeteoWeatherService> logger) : IWeatherService
{
    private const string Hourly = "temperature_2m,precipitation_probability,wind_speed_10m,wind_gusts_10m,weather_code";

    public async Task<HourlyWeather?> GetHourlyAsync(double latitude, double longitude, DateTimeOffset time, CancellationToken cancellationToken)
    {
        var hour = NearestHour(time);
        var lat = Math.Round(latitude, 2);
        var lon = Math.Round(longitude, 2);
        var date = DateOnly.FromDateTime(hour.UtcDateTime);
        var key = string.Create(CultureInfo.InvariantCulture, $"travel-info:weather:{lat:F2}:{lon:F2}:{date:yyyy-MM-dd}");

        try
        {
            var day = await cache.GetOrCreateAsync(
                key,
                (lat, lon, date, service: this),
                static (state, ct) => state.service.FetchDayAsync(state.lat, state.lon, state.date, ct),
                new HybridCacheEntryOptions { Expiration = options.Value.CacheDuration, LocalCacheExpiration = options.Value.CacheDuration },
                cancellationToken: cancellationToken);
            return day.FirstOrDefault(h => h.Time == hour);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogProviderFailed(ex);
            return null;
        }
    }

    private async ValueTask<List<HourlyWeather>> FetchDayAsync(double lat, double lon, DateOnly date, CancellationToken cancellationToken)
    {
        var http = httpClients.CreateClient(TravelInfoHttpClients.Weather);
        var url = string.Create(CultureInfo.InvariantCulture,
            $"forecast?latitude={lat:F2}&longitude={lon:F2}&hourly={Hourly}&timezone=GMT&start_date={date:yyyy-MM-dd}&end_date={date:yyyy-MM-dd}");
        var response = await http.GetFromJsonAsync<OpenMeteoForecast>(url, cancellationToken);
        var h = response?.Hourly ?? throw new InvalidDataException("Weather response has no hourly data.");

        var hours = new List<HourlyWeather>(h.Time.Count);
        for (var i = 0; i < h.Time.Count; i++)
        {
            // Skip hours the model has no values for rather than inventing zeros.
            if (At(h.Temperature, i) is not { } temperature || At(h.WindSpeed, i) is not { } wind || At(h.WeatherCode, i) is not { } code)
            {
                continue;
            }

            var timestamp = DateTime.ParseExact(h.Time[i], "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            hours.Add(new HourlyWeather(new DateTimeOffset(timestamp, TimeSpan.Zero), temperature, At(h.PrecipitationProbability, i),
                wind, At(h.WindGusts, i) ?? wind, code));
        }

        return hours;
    }

    private static T? At<T>(IReadOnlyList<T?>? values, int index)
        where T : struct => values is not null && index < values.Count ? values[index] : null;

    private static DateTimeOffset NearestHour(DateTimeOffset time)
    {
        var utc = time.ToUniversalTime();
        var floor = new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
        return utc.Minute >= 30 ? floor.AddHours(1) : floor;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Weather provider failed; forecast hidden")]
    private partial void LogProviderFailed(Exception exception);

    private sealed record OpenMeteoForecast(OpenMeteoHourly? Hourly);

    private sealed record OpenMeteoHourly(
        [property: JsonPropertyName("time")] List<string> Time,
        [property: JsonPropertyName("temperature_2m")] List<double?>? Temperature,
        [property: JsonPropertyName("precipitation_probability")] List<int?>? PrecipitationProbability,
        [property: JsonPropertyName("wind_speed_10m")] List<double?>? WindSpeed,
        [property: JsonPropertyName("wind_gusts_10m")] List<double?>? WindGusts,
        [property: JsonPropertyName("weather_code")] List<int?>? WeatherCode);
}
