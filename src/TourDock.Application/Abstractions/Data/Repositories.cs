using TourDock.Domain.Administration;
using TourDock.Domain.Agencies;
using TourDock.Domain.Bookings;
using TourDock.Domain.Reviews;
using TourDock.Domain.Slots;
using TourDock.Domain.Tours;
using TourDock.Domain.Vehicles;

namespace TourDock.Application.Abstractions.Data;

// Every agency-owned lookup takes the agency id, so a query can never return another tenant's data.

public interface IUnitOfWork
{
    /// <summary>
    /// Saves all changes atomically. Throws <see cref="ConcurrencyConflictException"/> or <see cref="DuplicateKeyException"/>
    /// when the database rejects the save because of a concurrent change.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Forgets all loaded entities, so a retry starts from fresh database state.</summary>
    void Reset();

    /// <summary>Starts an explicit transaction (needed for row locks). Dispose without commit = rollback.</summary>
    Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}

public interface ITransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface IAgencyRepository
{
    Task<Agency?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Agency>> GetForMemberAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Admin listing, newest first.</summary>
    Task<(IReadOnlyList<Agency> Items, int TotalCount)> ListAsync(AgencyStatus? status, int page, int pageSize, CancellationToken cancellationToken);

    void Add(Agency agency);
}

public interface IVehicleRepository
{
    Task<Vehicle?> GetAsync(Guid agencyId, Guid vehicleId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Vehicle>> ListAsync(Guid agencyId, CancellationToken cancellationToken);

    /// <summary>True if the vehicle has upcoming departures or active schedule rules.</summary>
    Task<bool> IsInUseAsync(Guid vehicleId, DateTimeOffset now, CancellationToken cancellationToken);

    void Add(Vehicle vehicle);
}

public interface ITourRepository
{
    /// <summary>Loads the whole aggregate (seasonal prices and schedule rules).</summary>
    Task<Tour?> GetAsync(Guid agencyId, Guid tourId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Tour>> ListAsync(Guid agencyId, CancellationToken cancellationToken);

    /// <summary>Unscoped lookup (including seasonal prices) used when a traveler books.</summary>
    Task<Tour?> GetByIdAsync(Guid tourId, CancellationToken cancellationToken);

    /// <summary>Tours by id, including deleted ones (bookings outlive tours). For display only.</summary>
    Task<IReadOnlyDictionary<Guid, Tour>> GetManyForDisplayAsync(IEnumerable<Guid> tourIds, CancellationToken cancellationToken);

    /// <summary>All tours (any agency) that have at least one active schedule rule. Used by the generation job.</summary>
    Task<IReadOnlyList<Tour>> ListWithActiveRulesAsync(CancellationToken cancellationToken);

    void Add(Tour tour);
}

public interface ISlotRepository
{
    /// <summary>Unscoped lookup for travelers booking a departure.</summary>
    Task<Slot?> GetByIdAsync(Guid slotId, CancellationToken cancellationToken);

    /// <summary>
    /// Loads the departure and locks its row until the current transaction ends (SELECT ... FOR UPDATE), so concurrent
    /// bookings of the same departure queue up instead of failing. Requires an open transaction.
    /// </summary>
    Task<Slot?> GetByIdForUpdateAsync(Guid slotId, CancellationToken cancellationToken);

    Task<Slot?> GetAsync(Guid agencyId, Guid slotId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Slot>> ListAsync(
        Guid agencyId, DateTimeOffset from, DateTimeOffset to, Guid? tourId, CancellationToken cancellationToken);

    /// <summary>True if the vehicle already has a non-cancelled departure overlapping [start, end).</summary>
    Task<bool> IsVehicleBusyAsync(Guid vehicleId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken);

    /// <summary>Start times of departures already generated from a rule (from <paramref name="from"/> onwards).</summary>
    Task<HashSet<DateTimeOffset>> GetRuleStartTimesAsync(Guid ruleId, DateTimeOffset from, CancellationToken cancellationToken);

    void Add(Slot slot);
}

public interface IBookingRepository
{
    Task<Booking?> GetByIdAsync(Guid bookingId, CancellationToken cancellationToken);

    Task<Booking?> GetByIdempotencyKeyAsync(Guid customerId, string idempotencyKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<Booking>> ListForCustomerAsync(Guid customerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Booking>> ListForAgencyAsync(Guid agencyId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>Pending bookings whose payment hold has run out (oldest first).</summary>
    Task<IReadOnlyList<Guid>> GetExpiredHoldIdsAsync(DateTimeOffset now, int take, CancellationToken cancellationToken);

    /// <summary>Confirmed bookings whose departure has ended.</summary>
    Task<IReadOnlyList<Guid>> GetFinishedConfirmedIdsAsync(DateTimeOffset now, int take, CancellationToken cancellationToken);

    void Add(Booking booking);

    Task<IReadOnlyList<Booking>> GetActiveForSlotAsync(Guid slotId, CancellationToken cancellationToken);

    Task<bool> HasUpcomingActiveForTourAsync(Guid tourId, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IReviewRepository
{
    Task<Review?> GetByIdAsync(Guid reviewId, CancellationToken cancellationToken);

    Task<Review?> GetByBookingIdAsync(Guid bookingId, CancellationToken cancellationToken);

    void Add(Review review);

    void Remove(Review review);

    Task<bool> HasReportedAsync(Guid reviewId, Guid reporterId, CancellationToken cancellationToken);

    void AddReport(ReviewReport report);

    Task<IReadOnlyList<ReviewReport>> GetOpenReportsAsync(Guid reviewId, CancellationToken cancellationToken);
}

/// <summary>Append-only record of admin actions.</summary>
public interface IAdminAuditLog
{
    void Add(AdminAction action);
}
