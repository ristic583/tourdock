using FluentValidation;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Common;
using TourDock.Domain.Common;

namespace TourDock.Application.Agencies;

// Back-office overview KPIs, computed in the database instead of from two range listings in the browser.

/// <param name="From">First local departure date in the range (inclusive).</param>
/// <param name="To">Last local departure date in the range (inclusive).</param>
public sealed record GetAgencyStatsQuery(Guid AgencyId, DateOnly From, DateOnly To) : IAgencyRequest, IQuery<AgencyStatsResponse>;

/// <summary>Overview numbers for one agency.</summary>
/// <param name="Departures">Open departures in the range.</param>
/// <param name="VehiclesInUse">Distinct vehicles running those departures.</param>
/// <param name="SeatsOnSale">Total capacity of those departures.</param>
/// <param name="SeatsSold">Seats taken on those departures (booked or on hold; a private booking takes the whole capacity).</param>
/// <param name="OccupancyPercent">SeatsSold / SeatsOnSale, rounded (0 when nothing is on sale).</param>
/// <param name="Revenue">Money for bookings departing in the range (EUR, the platform currency).</param>
/// <param name="Today">Today's departures, by each tour's local date (independent of the range).</param>
/// <param name="PendingHolds">Bookings currently on hold waiting for payment (any departure date).</param>
public sealed record AgencyStatsResponse(
    DateOnly From,
    DateOnly To,
    int Departures,
    int VehiclesInUse,
    int SeatsOnSale,
    int SeatsSold,
    int OccupancyPercent,
    RevenueDto Revenue,
    TodayStatsDto Today,
    int PendingHolds);

/// <param name="Paid">Everything customers paid.</param>
/// <param name="Refunded">What was (or is being) given back on cancellations.</param>
/// <param name="Net">Paid minus refunded.</param>
public sealed record RevenueDto(MoneyDto Paid, MoneyDto Refunded, MoneyDto Net);

/// <param name="Guests">Guests of confirmed (or already completed) bookings on today's departures.</param>
public sealed record TodayStatsDto(int Departures, int Guests);

/// <summary>Read-side aggregates over departures and bookings (implemented in Infrastructure).</summary>
public interface IAgencyStatsReader
{
    Task<AgencyStatsResponse> GetAsync(Guid agencyId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

internal sealed class GetAgencyStatsValidator : AbstractValidator<GetAgencyStatsQuery>
{
    public const int MaxRangeDays = 366;

    public GetAgencyStatsValidator()
    {
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From);
        RuleFor(q => q).Must(q => q.To.DayNumber - q.From.DayNumber < MaxRangeDays)
            .WithName("to").WithMessage($"The date range must be at most {MaxRangeDays} days.");
    }
}

/// <summary>Members (and admins, read-only) only; others get "not found".</summary>
internal sealed class GetAgencyStatsHandler(IAgencyAccess access, IAgencyStatsReader stats) : IQueryHandler<GetAgencyStatsQuery, AgencyStatsResponse>
{
    public async Task<Result<AgencyStatsResponse>> Handle(GetAgencyStatsQuery query, CancellationToken cancellationToken)
    {
        var agency = await access.RequireAsync(query.AgencyId, AgencyAccessLevel.Read, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        return await stats.GetAsync(query.AgencyId, query.From, query.To, cancellationToken);
    }
}
