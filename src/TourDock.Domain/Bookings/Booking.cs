using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Domain.Bookings;

public enum BookingType
{
    Shared,
    Private,
}

public enum BookingStatus
{
    /// <summary>Seats are held while the customer pays. Expires at <see cref="Booking.HoldExpiresAt"/>.</summary>
    Pending,
    Confirmed,
    Completed,
    Cancelled,
    Expired,
}

public enum CancelledBy
{
    Customer,
    Agency,
    Admin,
}

/// <summary>
/// A customer's reservation on a departure. Guards its own lifecycle and refund calculation.
/// Price, cancellation policy and departure times are snapshots taken at booking time.
/// Seat changes on the departure are coordinated by <see cref="BookingService"/>.
/// </summary>
public sealed partial class Booking : Entity, IAuditable
{
    public static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Short reference people can quote, e.g. "TD-DA10-23B4": the last 8 hex digits of the id. Ids are UUIDv7, whose
    /// leading digits are a timestamp shared by bookings made in the same minute; the trailing ones are random.
    /// The web app formats it the same way (<c>bookingReference</c> in web/src/lib/my-bookings.ts).
    /// </summary>
    public static string ReferenceFor(Guid id)
    {
        var hex = id.ToString("N")[^8..].ToUpperInvariant();
        return $"TD-{hex[..4]}-{hex[4..]}";
    }

    private Booking(Guid id)
        : base(id) { }

    private Booking() { }

    public Guid SlotId { get; private set; }

    public Guid TourId { get; private set; }

    public Guid AgencyId { get; private set; }

    public Guid CustomerId { get; private set; }

    public BookingType Type { get; private set; }

    /// <summary>Number of guests. For private bookings this is informational; the whole departure is reserved.</summary>
    public int Guests { get; private set; }

    public BookingStatus Status { get; private set; }

    /// <summary>Per seat for shared bookings, per departure for private bookings.</summary>
    public Money UnitPrice { get; private set; } = null!;

    /// <summary>Price of what is currently booked.</summary>
    public Money TotalPrice { get; private set; } = null!;

    /// <summary>Amount charged when the booking was confirmed.</summary>
    public Money PaidAmount { get; private set; } = null!;

    public Money RefundedAmount { get; private set; } = null!;

    public CancellationPolicy CancellationPolicy { get; private set; } = null!;

    public DateTimeOffset SlotStartsAt { get; private set; }

    public DateTimeOffset SlotEndsAt { get; private set; }

    public DateTimeOffset HoldExpiresAt { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public CancelledBy? CancelledBy { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>
    /// Client-supplied key (Idempotency-Key header) that makes "create booking" safe to retry.
    /// Unique per customer: the same key always returns the same booking.
    /// </summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Holds seats on the departure (active bookings).</summary>
    public bool IsActive => Status is BookingStatus.Pending or BookingStatus.Confirmed;

    internal static Booking CreatePending(
        Guid slotId,
        Tour tour,
        Guid customerId,
        BookingType type,
        int guests,
        Money unitPrice,
        DateTimeOffset slotStartsAt,
        DateTimeOffset slotEndsAt,
        DateTimeOffset now,
        string? idempotencyKey)
    {
        var total = type == BookingType.Shared ? unitPrice.Multiply(guests) : unitPrice;
        return new Booking(NewId())
        {
            SlotId = slotId,
            TourId = tour.Id,
            AgencyId = tour.AgencyId,
            CustomerId = customerId,
            Type = type,
            Guests = guests,
            Status = BookingStatus.Pending,
            UnitPrice = unitPrice,
            TotalPrice = total,
            PaidAmount = Money.Zero(unitPrice.Currency),
            RefundedAmount = Money.Zero(unitPrice.Currency),
            CancellationPolicy = tour.CancellationPolicy,
            SlotStartsAt = slotStartsAt,
            SlotEndsAt = slotEndsAt,
            HoldExpiresAt = now + HoldDuration,
            IdempotencyKey = idempotencyKey,
        };
    }

    /// <summary>True if this booking was created from the same request (used to detect a reused idempotency key).</summary>
    public bool MatchesRequest(Guid slotId, BookingType type, int guests) => SlotId == slotId && Type == type && Guests == guests;

    public bool IsHoldExpired(DateTimeOffset now) => Status == BookingStatus.Pending && now >= HoldExpiresAt;

    public Result Complete(DateTimeOffset now)
    {
        if (Status != BookingStatus.Confirmed)
        {
            return BookingErrors.InvalidStatus(Status, "completed");
        }

        if (now < SlotEndsAt)
        {
            return BookingErrors.NotFinishedYet;
        }

        Status = BookingStatus.Completed;
        CompletedAt = now;
        Raise(new BookingCompleted(Id));
        return Result.Success();
    }

    internal Result Expire(DateTimeOffset now)
    {
        if (!IsHoldExpired(now))
        {
            return BookingErrors.InvalidStatus(Status, "expired");
        }

        Status = BookingStatus.Expired;
        return Result.Success();
    }

    internal Result<Money> Cancel(CancelledBy by, string? reason, DateTimeOffset now)
    {
        if (!IsActive)
        {
            return BookingErrors.InvalidStatus(Status, "cancelled");
        }

        if (by == Bookings.CancelledBy.Customer && now >= SlotStartsAt)
        {
            return BookingErrors.AlreadyStarted;
        }

        // Customers get what the policy allows; agency/admin cancellations refund everything that was paid.
        var refund = by == Bookings.CancelledBy.Customer
            ? CustomerRefundFor(TotalPrice, now)
            : PaidAmount.Subtract(RefundedAmount);

        Status = BookingStatus.Cancelled;
        CancelledAt = now;
        CancelledBy = by;
        CancellationReason = reason;
        RefundedAmount = RefundedAmount.Add(refund);
        Raise(new BookingCancelled(Id, by, refund));
        return refund;
    }

    /// <summary>Partial cancellation: lowers the number of guests. Returns the refund for the removed seats.</summary>
    internal Result<Money> ReduceGuests(int newGuests, DateTimeOffset now)
    {
        if (!IsActive)
        {
            return BookingErrors.InvalidStatus(Status, "changed");
        }

        if (now >= SlotStartsAt)
        {
            return BookingErrors.AlreadyStarted;
        }

        if (newGuests < 1 || newGuests >= Guests)
        {
            return BookingErrors.InvalidGuestReduction(Guests);
        }

        var removed = Guests - newGuests;
        Guests = newGuests;

        if (Type == BookingType.Private)
        {
            // The whole departure is still reserved; the price is per departure.
            var none = Money.Zero(UnitPrice.Currency);
            Raise(new BookingGuestsReduced(Id, removed, none));
            return none;
        }

        var removedValue = UnitPrice.Multiply(removed);
        var refund = CustomerRefundFor(removedValue, now);
        TotalPrice = TotalPrice.Subtract(removedValue);
        RefundedAmount = RefundedAmount.Add(refund);
        Raise(new BookingGuestsReduced(Id, removed, refund));
        return refund;
    }
}

public static class BookingErrors
{
    public static readonly Error HoldExpired =
        Error.Conflict("Booking.HoldExpired", "The reservation hold has expired. Please book again.");

    public static readonly Error NotFinishedYet = Error.Conflict("Booking.NotFinishedYet", "The departure has not finished yet.");

    public static readonly Error AlreadyStarted = Error.Conflict("Booking.AlreadyStarted", "The departure has already started.");

    public static readonly Error SlotMismatch =
        Error.Validation("Booking.SlotMismatch", "The booking does not belong to this departure.");

    public static Error InvalidStatus(BookingStatus status, string action) =>
        Error.Conflict("Booking.InvalidStatus", $"A {status.ToString().ToLowerInvariant()} booking cannot be {action}.");

    public static Error InvalidGuestReduction(int current) =>
        Error.Validation("Booking.InvalidGuestReduction", $"The new number of guests must be between 1 and {current - 1}.");
}
