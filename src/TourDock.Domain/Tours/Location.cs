using TourDock.Domain.Common;

namespace TourDock.Domain.Tours;

/// <summary>Where a tour starts. <see cref="TimeZoneId"/> is an IANA id (e.g. "Europe/Zagreb") used for all local times.</summary>
public sealed record Location
{
    private Location(string countryCode, string city, string address, double latitude, double longitude, string timeZoneId)
    {
        CountryCode = countryCode;
        City = city;
        Address = address;
        Latitude = latitude;
        Longitude = longitude;
        TimeZoneId = timeZoneId;
    }

    /// <summary>ISO 3166-1 alpha-2, upper case.</summary>
    public string CountryCode { get; private set; }

    public string City { get; private set; }

    public string Address { get; private set; }

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public string TimeZoneId { get; private set; }

    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    public static Result<Location> Create(
        string countryCode, string city, string address, double latitude, double longitude, string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2 || !countryCode.All(char.IsAsciiLetter))
        {
            return TourErrors.InvalidLocation("Country code must be a 2-letter ISO code.");
        }

        if (string.IsNullOrWhiteSpace(city))
        {
            return TourErrors.InvalidLocation("City is required.");
        }

        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            return TourErrors.InvalidLocation("Coordinates are out of range.");
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone) || !timeZone.HasIanaId)
        {
            return TourErrors.InvalidLocation($"Unknown IANA time zone '{timeZoneId}'.");
        }

        return new Location(countryCode.ToUpperInvariant(), city.Trim(), address.Trim(), latitude, longitude, timeZoneId);
    }
}
