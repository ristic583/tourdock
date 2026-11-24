using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Domain.Slots;

public enum SlotStatus
{
    /// <summary>Open for booking (subject to capacity).</summary>
    Open,

    /// <summary>Temporarily not bookable (e.g. weather). Only possible while nothing is reserved.</summary>
    Blocked,

    /// <summary>Cancelled by the agency. Frees the vehicle; existing bookings must be cancelled and refunded.</summary>
    Cancelled,
}

/// <summary>
/// A concrete departure of a tour, with one vehicle. The aggregate root that guards capacity:
/// every seat reservation and release goes through it, and its row version makes concurrent
/// reservations conflict instead of overbooking.
/// </summary>
public sealed class Slot : Entity, IAuditable
{
    private Slot(Guid id)
        : base(id) { }

    private Slot() { }

    public Guid TourId { get; private set; }

    public Guid AgencyId { get; private set; }

    public Guid VehicleId { get; private set; }

    /// <summary>Set when generated from a schedule rule; null for manually created departures.</summary>
    public Guid? ScheduleRuleId { get; private set; }

    /// <summary>UTC.</summary>
    public DateTimeOffset StartsAt { get; private set; }

    /// <summary>UTC.</summary>
    public DateTimeOffset EndsAt { get; private set; }

    /// <summary>Local date at the tour's location; used for seasonal pricing.</summary>
    public DateOnly LocalDate { get; private set; }

    /// <summary>Fixed when the departure is created: min(tour max guests, vehicle capacity).</summary>
    public int Capacity { get; private set; }

    public int ReservedSeats { get; private set; }

    /// <summary>Booking mode of the tour when the departure was created.</summary>
    public BookingMode BookingMode { get; private set; }

    public bool IsPrivatelyBooked { get; private set; }

    public SlotStatus Status { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public int RemainingSeats => Capacity - ReservedSeats;

    public static Result<Slot> Create(
        Tour tour, Guid vehicleId, int vehicleCapacity, DateTimeOffset startsAt, DateTimeOffset now, Guid? scheduleRuleId = null)
    {
        if (startsAt <= now)
        {
            return SlotErrors.StartInPast;
        }

        var capacity = Math.Min(tour.MaxGuests, vehicleCapacity);
        if (capacity < 1)
        {
            return SlotErrors.InvalidCapacity;
        }

        var startUtc = startsAt.ToUniversalTime();
        return new Slot(NewId())
        {
            TourId = tour.Id,
            AgencyId = tour.AgencyId,
            VehicleId = vehicleId,
            ScheduleRuleId = scheduleRuleId,
            StartsAt = startUtc,
            EndsAt = startUtc + tour.Duration,
            LocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startUtc, tour.Location.TimeZone).DateTime),
            Capacity = capacity,
            BookingMode = tour.BookingMode,
            Status = SlotStatus.Open,
        };
    }

    public bool HasStarted(DateTimeOffset now) => now >= StartsAt;

    /// <summary>Reserves seats for a shared booking.</summary>
    public Result ReserveSeats(int seats, DateTimeOffset now)
    {
        var bookable = EnsureBookable(now);
        if (bookable.IsFailure)
        {
            return bookable;
        }

        if (!Tour.AllowsShared(BookingMode))
        {
            return SlotErrors.SharedNotAllowed;
        }

        if (seats < 1)
        {
            return SlotErrors.InvalidSeats;
        }

        if (IsPrivatelyBooked || seats > RemainingSeats)
        {
            return SlotErrors.NotEnoughSeats(RemainingSeats);
        }

        ReservedSeats += seats;
        return Result.Success();
    }

    /// <summary>Reserves the whole departure for one group. Only possible while no seat has been sold.</summary>
    public Result ReserveWhole(int groupSize, DateTimeOffset now)
    {
        var bookable = EnsureBookable(now);
        if (bookable.IsFailure)
        {
            return bookable;
        }

        if (!Tour.AllowsPrivate(BookingMode))
        {
            return SlotErrors.PrivateNotAllowed;
        }

        if (groupSize < 1 || groupSize > Capacity)
        {
            return SlotErrors.GroupTooLarge(Capacity);
        }

        if (ReservedSeats > 0)
        {
            return SlotErrors.AlreadyPartiallyBooked;
        }

        ReservedSeats = Capacity;
        IsPrivatelyBooked = true;
        return Result.Success();
    }

    public void ReleaseSeats(int seats)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seats);
        if (IsPrivatelyBooked)
        {
            throw new InvalidOperationException("Use ReleaseWhole for a privately booked departure.");
        }

        ReservedSeats = Math.Max(0, ReservedSeats - seats);
    }

    public void ReleaseWhole()
    {
        ReservedSeats = 0;
        IsPrivatelyBooked = false;
    }

    public Result Block()
    {
        if (Status != SlotStatus.Open)
        {
            return SlotErrors.NotOpen;
        }

        if (ReservedSeats > 0)
        {
            return SlotErrors.HasReservations;
        }

        Status = SlotStatus.Blocked;
        return Result.Success();
    }

    public Result Unblock()
    {
        if (Status != SlotStatus.Blocked)
        {
            return SlotErrors.NotBlocked;
        }

        Status = SlotStatus.Open;
        return Result.Success();
    }

    /// <summary>Cancels the departure. The caller must cancel (and fully refund) its bookings in the same transaction.</summary>
    public Result Cancel(DateTimeOffset now)
    {
        if (Status == SlotStatus.Cancelled)
        {
            return SlotErrors.AlreadyCancelled;
        }

        if (HasStarted(now))
        {
            return SlotErrors.AlreadyStarted;
        }

        Status = SlotStatus.Cancelled;
        ReleaseWhole();
        return Result.Success();
    }

    private Result EnsureBookable(DateTimeOffset now)
    {
        if (Status != SlotStatus.Open)
        {
            return SlotErrors.NotOpen;
        }

        return HasStarted(now) ? SlotErrors.AlreadyStarted : Result.Success();
    }
}

public static class SlotErrors
{
    public static readonly Error StartInPast = Error.Validation("Slot.StartInPast", "A departure must start in the future.");

    public static readonly Error InvalidCapacity = Error.Validation("Slot.InvalidCapacity", "A departure needs a capacity of at least 1.");

    public static readonly Error InvalidSeats = Error.Validation("Slot.InvalidSeats", "At least one seat must be booked.");

    public static readonly Error SharedNotAllowed =
        Error.Conflict("Slot.SharedNotAllowed", "This departure can only be booked privately.");

    public static readonly Error PrivateNotAllowed =
        Error.Conflict("Slot.PrivateNotAllowed", "This departure can only be booked per seat.");

    public static readonly Error AlreadyPartiallyBooked =
        Error.Conflict("Slot.AlreadyPartiallyBooked", "Seats on this departure are already sold, so it can no longer be booked privately.");

    public static readonly Error NotOpen = Error.Conflict("Slot.NotOpen", "This departure is not open for booking.");

    public static readonly Error NotBlocked = Error.Conflict("Slot.NotBlocked", "This departure is not blocked.");

    public static readonly Error HasReservations =
        Error.Conflict("Slot.HasReservations", "A departure with reservations cannot be blocked; cancel it instead.");

    public static readonly Error AlreadyCancelled = Error.Conflict("Slot.AlreadyCancelled", "This departure is already cancelled.");

    public static readonly Error AlreadyStarted = Error.Conflict("Slot.AlreadyStarted", "This departure has already started.");

    public static Error NotEnoughSeats(int remaining) =>
        Error.Conflict("Slot.NotEnoughSeats", $"Not enough seats left on this departure ({remaining} remaining).");

    public static Error GroupTooLarge(int capacity) =>
        Error.Validation("Slot.GroupTooLarge", $"Group size must be between 1 and {capacity}.");
}
