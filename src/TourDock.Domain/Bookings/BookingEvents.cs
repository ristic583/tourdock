using TourDock.Domain.Common;

namespace TourDock.Domain.Bookings;

/// <summary>Payment went through and the seats are the customer's.</summary>
public sealed record BookingConfirmed(Guid BookingId) : IDomainEvent;

/// <param name="Refund">What the customer gets back (zero when the policy allows nothing).</param>
public sealed record BookingCancelled(Guid BookingId, CancelledBy By, Money Refund) : IDomainEvent;

/// <param name="RemovedGuests">How many guests were taken off the booking.</param>
public sealed record BookingGuestsReduced(Guid BookingId, int RemovedGuests, Money Refund) : IDomainEvent;

/// <summary>The departure is over; the traveler can now review it.</summary>
public sealed record BookingCompleted(Guid BookingId) : IDomainEvent;
