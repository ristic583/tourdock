using Microsoft.EntityFrameworkCore;
using TourDock.Application.Agencies;
using TourDock.Application.Common;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Slots;
using TourDock.Infrastructure.Persistence;

namespace TourDock.Infrastructure.Agencies;

/// <summary>
/// Aggregates in PostgreSQL (no entities loaded). Departures are selected by their local date at the tour's location;
/// the UTC range is padded by a day on each side first so the (agency_id, starts_at) indexes do the narrowing.
/// </summary>
internal sealed class AgencyStatsReader(TourDockDbContext db, TimeProvider time) : IAgencyStatsReader
{
    public async Task<AgencyStatsResponse> GetAsync(Guid agencyId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var (utcFrom, utcTo) = Padded(from, to);

        var open = db.Slots.AsNoTracking()
            .Where(s => s.AgencyId == agencyId && s.StartsAt >= utcFrom && s.StartsAt < utcTo)
            .Where(s => s.LocalDate >= from && s.LocalDate <= to && s.Status == SlotStatus.Open);

        var seats = await open
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Departures = g.Count(),
                OnSale = g.Sum(s => s.Capacity),
                Sold = g.Sum(s => s.IsPrivatelyBooked ? s.Capacity : s.ReservedSeats),
            })
            .SingleOrDefaultAsync(cancellationToken);
        var vehicles = await open.Select(s => s.VehicleId).Distinct().CountAsync(cancellationToken);

        var revenue = await db.Bookings.AsNoTracking()
            .Where(b => b.AgencyId == agencyId && b.SlotStartsAt >= utcFrom && b.SlotStartsAt < utcTo)
            .Join(db.Slots, b => b.SlotId, s => s.Id, (b, s) => new { Booking = b, s.LocalDate })
            .Where(x => x.LocalDate >= from && x.LocalDate <= to)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Paid = g.Sum(x => x.Booking.PaidAmount.Amount),
                Refunded = g.Sum(x => x.Booking.RefundedAmount.Amount),
            })
            .SingleOrDefaultAsync(cancellationToken);

        var now = time.GetUtcNow();
        var pendingHolds = await db.Bookings.AsNoTracking()
            .CountAsync(b => b.AgencyId == agencyId && b.Status == BookingStatus.Pending && b.HoldExpiresAt > now, cancellationToken);

        var onSale = seats?.OnSale ?? 0;
        var sold = seats?.Sold ?? 0;
        return new AgencyStatsResponse(
            from,
            to,
            seats?.Departures ?? 0,
            vehicles,
            onSale,
            sold,
            onSale == 0 ? 0 : (int)Math.Round(sold * 100.0 / onSale, MidpointRounding.AwayFromZero),
            Revenue(revenue?.Paid ?? 0, revenue?.Refunded ?? 0),
            await TodayAsync(agencyId, now, cancellationToken),
            pendingHolds);
    }

    /// <summary>"Today" differs per tour time zone, so it is resolved per tour before counting.</summary>
    private async Task<TodayStatsDto> TodayAsync(Guid agencyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Deleted tours still have departures and guests today.
        var zones = await db.Tours.AsNoTracking().IgnoreQueryFilters()
            .Where(t => t.AgencyId == agencyId)
            .Select(t => new { t.Id, t.Location.TimeZoneId })
            .ToListAsync(cancellationToken);
        var todayByTour = zones.ToDictionary(
            t => t.Id, t => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(t.TimeZoneId)).DateTime));
        if (todayByTour.Count == 0)
        {
            return new TodayStatsDto(0, 0);
        }

        var dates = todayByTour.Values.Distinct().ToList();
        var (utcFrom, utcTo) = Padded(dates.Min(), dates.Max());
        var candidates = await db.Slots.AsNoTracking()
            .Where(s => s.AgencyId == agencyId && s.StartsAt >= utcFrom && s.StartsAt < utcTo && dates.Contains(s.LocalDate))
            .Where(s => s.Status == SlotStatus.Open)
            .Select(s => new { s.Id, s.TourId, s.LocalDate })
            .ToListAsync(cancellationToken);
        var todays = candidates.Where(s => todayByTour.GetValueOrDefault(s.TourId) == s.LocalDate).Select(s => s.Id).ToList();
        if (todays.Count == 0)
        {
            return new TodayStatsDto(0, 0);
        }

        var guests = await db.Bookings.AsNoTracking()
            .Where(b => todays.Contains(b.SlotId) && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed))
            .SumAsync(b => b.Guests, cancellationToken);
        return new TodayStatsDto(todays.Count, guests);
    }

    // All money on the platform is in EUR (ADR 0006).
    private static RevenueDto Revenue(decimal paid, decimal refunded) =>
        new(new MoneyDto(paid, Currencies.Eur), new MoneyDto(refunded, Currencies.Eur), new MoneyDto(paid - refunded, Currencies.Eur));

    private static (DateTimeOffset From, DateTimeOffset To) Padded(DateOnly from, DateOnly to) =>
        (new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1),
         new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(2));
}
