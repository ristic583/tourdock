using System.ComponentModel.DataAnnotations;

namespace TourDock.Infrastructure.TravelInfo;

/// <summary>Frankfurter (ECB reference rates). Free, no API key. Rates are published once per working day.</summary>
public sealed class ExchangeRatesOptions
{
    public const string SectionName = "ExchangeRates";

    [Required]
    [Url]
    public string BaseUrl { get; init; } = "https://api.frankfurter.dev/v1/";

    /// <summary>How long fetched rates are served before the provider is asked again.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromHours(6);

    /// <summary>How long the last good rates are kept as a fallback while the provider is down.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "30.00:00:00")]
    public TimeSpan StaleFallbackDuration { get; init; } = TimeSpan.FromDays(7);
}

/// <summary>Open-Meteo forecast API. Free for non-commercial use, no API key (data CC BY 4.0).</summary>
public sealed class WeatherOptions
{
    public const string SectionName = "Weather";

    [Required]
    [Url]
    public string BaseUrl { get; init; } = "https://api.open-meteo.com/v1/";

    /// <summary>Open-Meteo refreshes its models roughly hourly.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromHours(1);
}

/// <summary>Names of the HTTP clients, so tests can swap their primary handler for a stub.</summary>
public static class TravelInfoHttpClients
{
    public const string ExchangeRates = "frankfurter";
    public const string Weather = "open-meteo";
}
