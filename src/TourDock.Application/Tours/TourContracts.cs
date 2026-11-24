using FluentValidation;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Domain.Agencies;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Tours;

public static class TourUseCaseErrors
{
    public static readonly Error NotFound = Error.NotFound("Tour.NotFound", "Tour not found.");

    public static readonly Error HasUpcomingBookings =
        Error.Conflict("Tour.HasUpcomingBookings", "The tour has upcoming bookings. Cancel its departures first.");
}

internal static class TourRules
{
    public static void ValidPrice<T>(this IRuleBuilder<T, decimal?> rule) =>
        rule.GreaterThan(0).LessThanOrEqualTo(1_000_000m).PrecisionScale(12, 2, ignoreTrailingZeros: true);
}

internal static class TourInput
{
    public static Result<Location> ToLocation(LocationDto l) =>
        Location.Create(l.CountryCode, l.City, l.Address, l.Latitude, l.Longitude, l.TimeZoneId);

    public static Result<CancellationPolicy> ToPolicy(IReadOnlyList<RefundTierDto> tiers) =>
        CancellationPolicy.Create(tiers.Select(t => new RefundTier(t.HoursBeforeStart, t.RefundPercent)));
}
