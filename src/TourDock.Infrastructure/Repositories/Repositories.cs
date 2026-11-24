using Microsoft.EntityFrameworkCore;
using TourDock.Application.Abstractions.Data;
using TourDock.Domain.Bookings;
using TourDock.Domain.Slots;
using TourDock.Domain.Tours;
using TourDock.Domain.Vehicles;
using TourDock.Infrastructure.Persistence;

namespace TourDock.Infrastructure.Repositories;

internal sealed class VehicleRepository(TourDockDbContext db) : IVehicleRepository
{
    public Task<Vehicle?> GetAsync(Guid agencyId, Guid vehicleId, CancellationToken cancellationToken) =>
        db.Vehicles.SingleOrDefaultAsync(v => v.Id == vehicleId && v.AgencyId == agencyId, cancellationToken);

    public async Task<IReadOnlyList<Vehicle>> ListAsync(Guid agencyId, CancellationToken cancellationToken) =>
        await db.Vehicles.AsNoTracking().Where(v => v.AgencyId == agencyId).OrderBy(v => v.Name).ToListAsync(cancellationToken);

    public async Task<bool> IsInUseAsync(Guid vehicleId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Slots.AnyAsync(s => s.VehicleId == vehicleId && s.EndsAt > now && s.Status != SlotStatus.Cancelled, cancellationToken)
        || await db.Set<ScheduleRule>().AnyAsync(r => r.VehicleId == vehicleId && r.IsActive, cancellationToken);

    public void Add(Vehicle vehicle) => db.Vehicles.Add(vehicle);
}

internal sealed class TourRepository(TourDockDbContext db) : ITourRepository
{
    public Task<Tour?> GetByIdAsync(Guid tourId, CancellationToken cancellationToken) =>
        db.Tours.Include(t => t.SeasonalPrices).Include(t => t.Photos).AsSplitQuery().SingleOrDefaultAsync(t => t.Id == tourId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Tour>> GetManyForDisplayAsync(IEnumerable<Guid> tourIds, CancellationToken cancellationToken)
    {
        var ids = tourIds.Distinct().ToList();
        return await db.Tours.AsNoTracking().IgnoreQueryFilters().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);
    }

    public Task<Tour?> GetAsync(Guid agencyId, Guid tourId, CancellationToken cancellationToken) =>
        db.Tours
            .Include(t => t.SeasonalPrices)
            .Include(t => t.ScheduleRules)
            .Include(t => t.Photos)
            .AsSplitQuery()
            .SingleOrDefaultAsync(t => t.Id == tourId && t.AgencyId == agencyId, cancellationToken);

    public async Task<IReadOnlyList<Tour>> ListAsync(Guid agencyId, CancellationToken cancellationToken) =>
        await db.Tours.AsNoTracking()
            .Include(t => t.SeasonalPrices)
            .Include(t => t.ScheduleRules)
            .Include(t => t.Photos)
            .AsSplitQuery()
            .Where(t => t.AgencyId == agencyId)
            .OrderBy(t => t.Title)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Tour>> ListWithActiveRulesAsync(CancellationToken cancellationToken) =>
        await db.Tours
            .Include(t => t.SeasonalPrices)
            .Include(t => t.ScheduleRules)
            .Include(t => t.Photos)
            .AsSplitQuery()
            .Where(t => t.ScheduleRules.Any(r => r.IsActive))
            .ToListAsync(cancellationToken);

    public void Add(Tour tour) => db.Tours.Add(tour);
}

internal sealed class SlotRepository(TourDockDbContext db) : ISlotRepository
{
    public Task<Slot?> GetByIdAsync(Guid slotId, CancellationToken cancellationToken) =>
        db.Slots.SingleOrDefaultAsync(s => s.Id == slotId, cancellationToken);

    public async Task<Slot?> GetByIdForUpdateAsync(Guid slotId, CancellationToken cancellationToken)
    {
        // Don't let a request wait forever behind a stuck transaction. xmin is the row version EF needs (not part of "*").
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", cancellationToken);
        return await db.Slots.FromSql($"SELECT *, xmin FROM slots WHERE id = {slotId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
    }

    public Task<Slot?> GetAsync(Guid agencyId, Guid slotId, CancellationToken cancellationToken) =>
        db.Slots.SingleOrDefaultAsync(s => s.Id == slotId && s.AgencyId == agencyId, cancellationToken);

    public async Task<IReadOnlyList<Slot>> ListAsync(
        Guid agencyId, DateTimeOffset from, DateTimeOffset to, Guid? tourId, CancellationToken cancellationToken) =>
        await db.Slots.AsNoTracking()
            .Where(s => s.AgencyId == agencyId && s.StartsAt >= from && s.StartsAt < to)
            .Where(s => tourId == null || s.TourId == tourId)
            .OrderBy(s => s.StartsAt)
            .ToListAsync(cancellationToken);

    // Same rule as the exclusion constraint, checked up front for a friendly error; the constraint still guards races.
    public Task<bool> IsVehicleBusyAsync(Guid vehicleId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken) =>
        db.Slots.AnyAsync(
            s => s.VehicleId == vehicleId && s.Status != SlotStatus.Cancelled && s.StartsAt < end && s.EndsAt > start,
            cancellationToken);

    public async Task<HashSet<DateTimeOffset>> GetRuleStartTimesAsync(Guid ruleId, DateTimeOffset from, CancellationToken cancellationToken) =>
        [.. await db.Slots.Where(s => s.ScheduleRuleId == ruleId && s.StartsAt >= from).Select(s => s.StartsAt).ToListAsync(cancellationToken)];

    public void Add(Slot slot) => db.Slots.Add(slot);
}

internal sealed class BookingRepository(TourDockDbContext db) : IBookingRepository
{
    public Task<Booking?> GetByIdAsync(Guid bookingId, CancellationToken cancellationToken) =>
        db.Bookings.SingleOrDefaultAsync(b => b.Id == bookingId, cancellationToken);

    public Task<Booking?> GetByIdempotencyKeyAsync(Guid customerId, string idempotencyKey, CancellationToken cancellationToken) =>
        db.Bookings.SingleOrDefaultAsync(b => b.CustomerId == customerId && b.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<IReadOnlyList<Booking>> ListForCustomerAsync(Guid customerId, CancellationToken cancellationToken) =>
        await db.Bookings.AsNoTracking().Where(b => b.CustomerId == customerId).OrderByDescending(b => b.SlotStartsAt).Take(200)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Booking>> ListForAgencyAsync(Guid agencyId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        await db.Bookings.AsNoTracking()
            .Where(b => b.AgencyId == agencyId && b.SlotStartsAt >= from && b.SlotStartsAt < to)
            .OrderBy(b => b.SlotStartsAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetExpiredHoldIdsAsync(DateTimeOffset now, int take, CancellationToken cancellationToken) =>
        await db.Bookings.Where(b => b.Status == BookingStatus.Pending && b.HoldExpiresAt <= now)
            .OrderBy(b => b.HoldExpiresAt).Select(b => b.Id).Take(take).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetFinishedConfirmedIdsAsync(DateTimeOffset now, int take, CancellationToken cancellationToken) =>
        await db.Bookings.Where(b => b.Status == BookingStatus.Confirmed && b.SlotEndsAt <= now)
            .OrderBy(b => b.SlotEndsAt).Select(b => b.Id).Take(take).ToListAsync(cancellationToken);

    public void Add(Booking booking) => db.Bookings.Add(booking);

    public async Task<IReadOnlyList<Booking>> GetActiveForSlotAsync(Guid slotId, CancellationToken cancellationToken) =>
        await db.Bookings
            .Where(b => b.SlotId == slotId && (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed))
            .ToListAsync(cancellationToken);

    public Task<bool> HasUpcomingActiveForTourAsync(Guid tourId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Bookings.AnyAsync(
            b => b.TourId == tourId && b.SlotStartsAt > now && (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed),
            cancellationToken);
}
