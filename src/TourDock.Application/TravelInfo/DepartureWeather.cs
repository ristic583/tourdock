using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Domain.Common;
using TourDock.Domain.Slots;

namespace TourDock.Application.TravelInfo;

public enum WeatherForecastStatus
{
    /// <summary>A forecast for the departure hour is included.</summary>
    Available,

    /// <summary>The departure is further out than the forecast horizon; see <c>ForecastAvailableFrom</c>.</summary>
    NotYetAvailable,

    /// <summary>The departure has already started.</summary>
    Past,

    /// <summary>The weather provider could not be reached. The page should simply hide the forecast.</summary>
    Unavailable,
}

/// <param name="ForecastAvailableFrom">When <see cref="WeatherForecastStatus.NotYetAvailable"/>: from when to ask again.</param>
public sealed record DepartureWeatherResponse(
    Guid DepartureId,
    DateTimeOffset StartsAt,
    WeatherForecastStatus Status,
    WeatherForecastDto? Forecast,
    DateTimeOffset? ForecastAvailableFrom);

/// <param name="Time">The forecast hour (UTC) nearest to the departure.</param>
/// <param name="RoughConditions">Strong wind, gusts or thunderstorms: worth a warning for boat and outdoor tours.</param>
public sealed record WeatherForecastDto(
    DateTimeOffset Time,
    double TemperatureC,
    int? PrecipitationProbability,
    double WindSpeedKmh,
    double WindGustsKmh,
    int WeatherCode,
    string Summary,
    bool RoughConditions);

public sealed record GetDepartureWeatherQuery(Guid DepartureId) : IQuery<DepartureWeatherResponse>;

public static class TravelInfoErrors
{
    public static readonly Error DepartureNotFound = Error.NotFound("Departure.NotFound", "Departure not found.");
}

internal sealed class GetDepartureWeatherHandler(ISlotRepository slots, ITourRepository tours, IWeatherService weather, TimeProvider time)
    : IQueryHandler<GetDepartureWeatherQuery, DepartureWeatherResponse>
{
    /// <summary>Hourly forecasts further out than this are too unreliable to show (Open-Meteo offers up to 16 days).</summary>
    public static readonly TimeSpan ForecastHorizon = TimeSpan.FromDays(14);

    public async Task<Result<DepartureWeatherResponse>> Handle(GetDepartureWeatherQuery query, CancellationToken cancellationToken)
    {
        var slot = await slots.GetByIdAsync(query.DepartureId, cancellationToken);
        if (slot is null || slot.Status == SlotStatus.Cancelled)
        {
            return TravelInfoErrors.DepartureNotFound;
        }

        var tour = await tours.GetByIdAsync(slot.TourId, cancellationToken);
        if (tour is null || !tour.IsBookable)
        {
            return TravelInfoErrors.DepartureNotFound;
        }

        var now = time.GetUtcNow();
        if (slot.StartsAt <= now)
        {
            return Response(WeatherForecastStatus.Past);
        }

        if (slot.StartsAt - now > ForecastHorizon)
        {
            return Response(WeatherForecastStatus.NotYetAvailable, availableFrom: slot.StartsAt - ForecastHorizon);
        }

        var hourly = await weather.GetHourlyAsync(tour.Location.Latitude, tour.Location.Longitude, slot.StartsAt, cancellationToken);
        return hourly is null
            ? Response(WeatherForecastStatus.Unavailable)
            : Response(WeatherForecastStatus.Available, WeatherCodes.ToDto(hourly));

        DepartureWeatherResponse Response(WeatherForecastStatus status, WeatherForecastDto? forecast = null, DateTimeOffset? availableFrom = null) =>
            new(slot.Id, slot.StartsAt, status, forecast, availableFrom);
    }
}

/// <summary>Plain-language summaries for WMO weather codes, and the "rough conditions" rule.</summary>
public static class WeatherCodes
{
    public const double RoughWindKmh = 30;
    public const double RoughGustsKmh = 45;

    public static WeatherForecastDto ToDto(HourlyWeather w) =>
        new(w.Time, w.TemperatureC, w.PrecipitationProbability, w.WindSpeedKmh, w.WindGustsKmh, w.WeatherCode,
            Summary(w.WeatherCode), IsRough(w));

    public static bool IsRough(HourlyWeather w) =>
        w.WindSpeedKmh >= RoughWindKmh || w.WindGustsKmh >= RoughGustsKmh || w.WeatherCode is >= 95 and <= 99;

    public static string Summary(int code) => code switch
    {
        0 => "Clear sky",
        1 => "Mainly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        >= 51 and <= 57 => "Drizzle",
        >= 61 and <= 67 => "Rain",
        >= 71 and <= 77 => "Snow",
        >= 80 and <= 82 => "Rain showers",
        85 or 86 => "Snow showers",
        >= 95 and <= 99 => "Thunderstorm",
        _ => "Unknown",
    };
}
