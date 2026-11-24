using TourDock.Application.Abstractions;
using TourDock.Application.Common;
using TourDock.Application.Photos;
using TourDock.Domain.Tours;

namespace TourDock.Application.Tours;

public sealed record LocationDto(string CountryCode, string City, string Address, double Latitude, double Longitude, string TimeZoneId);

public sealed record RefundTierDto(int HoursBeforeStart, int RefundPercent);

public sealed record SeasonalPriceDto(Guid Id, DateOnly From, DateOnly To, MoneyDto? PricePerPerson, MoneyDto? PrivatePrice);

public sealed record ScheduleRuleDto(
    Guid Id, Guid VehicleId, DaysOfWeek Days, TimeOnly StartTime, DateOnly ValidFrom, DateOnly? ValidUntil, bool IsActive);

public sealed record TourResponse(
    Guid Id,
    Guid AgencyId,
    string Title,
    string Description,
    LocationDto Location,
    int DurationMinutes,
    int MaxGuests,
    BookingMode BookingMode,
    MoneyDto? PricePerPerson,
    MoneyDto? PrivatePrice,
    IReadOnlyList<RefundTierDto> CancellationPolicy,
    TourStatus Status,
    IReadOnlyList<SeasonalPriceDto> SeasonalPrices,
    IReadOnlyList<ScheduleRuleDto> ScheduleRules,
    IReadOnlyList<PhotoDto> Photos);

public static class TourMapping
{
    public static TourResponse ToResponse(this Tour tour, IPhotoUrls urls) => new(
        tour.Id,
        tour.AgencyId,
        tour.Title,
        tour.Description,
        tour.Location.ToDto(),
        (int)tour.Duration.TotalMinutes,
        tour.MaxGuests,
        tour.BookingMode,
        tour.PricePerPerson.ToDto(),
        tour.PrivatePrice.ToDto(),
        tour.CancellationPolicy.Tiers.Select(t => new RefundTierDto(t.HoursBeforeStart, t.RefundPercent)).ToList(),
        tour.Status,
        tour.SeasonalPrices.OrderBy(s => s.From)
            .Select(s => new SeasonalPriceDto(s.Id, s.From, s.To, s.PricePerPerson.ToDto(), s.PrivatePrice.ToDto())).ToList(),
        tour.ScheduleRules
            .Select(r => new ScheduleRuleDto(r.Id, r.VehicleId, r.Days, r.StartTime, r.ValidFrom, r.ValidUntil, r.IsActive)).ToList(),
        tour.Photos.Select(p => p.ToDto(urls)).ToList());

    public static LocationDto ToDto(this Location l) => new(l.CountryCode, l.City, l.Address, l.Latitude, l.Longitude, l.TimeZoneId);
}
