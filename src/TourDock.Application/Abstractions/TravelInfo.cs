namespace TourDock.Application.Abstractions;

// Third-party data shown next to tours (exchange rates, weather). Display-only: nothing here may affect what a
// traveler is charged, and every implementation degrades to "no data" instead of failing the request.

/// <summary>Reference exchange rates for one base currency on one publication date.</summary>
/// <param name="Rates">Units of each quote currency per 1 unit of <paramref name="BaseCurrency"/>.</param>
/// <param name="IsStale">True when the provider was unreachable and these are the last rates we fetched.</param>
public sealed record ExchangeRatesSnapshot(string BaseCurrency, DateOnly Date, IReadOnlyDictionary<string, decimal> Rates, bool IsStale);

public interface IExchangeRateService
{
    /// <summary>Latest rates from the platform currency, or null when none are available at all.</summary>
    Task<ExchangeRatesSnapshot?> GetLatestAsync(CancellationToken cancellationToken);
}

/// <summary>Forecast for one hour at one place. Weather codes follow WMO 4677 (as used by Open-Meteo).</summary>
public sealed record HourlyWeather(
    DateTimeOffset Time,
    double TemperatureC,
    int? PrecipitationProbability,
    double WindSpeedKmh,
    double WindGustsKmh,
    int WeatherCode);

public interface IWeatherService
{
    /// <summary>
    /// Forecast for the hour nearest to <paramref name="time"/>, or null when the provider is unavailable or has no
    /// forecast for that hour.
    /// </summary>
    Task<HourlyWeather?> GetHourlyAsync(double latitude, double longitude, DateTimeOffset time, CancellationToken cancellationToken);
}
