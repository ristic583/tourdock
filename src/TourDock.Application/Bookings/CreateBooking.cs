using FluentValidation;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Abstractions.Payments;
using TourDock.Application.Common;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Bookings;

public sealed record CreateBookingCommand(
    Guid DepartureId, BookingType Type, int Guests, string IdempotencyKey)
    : ICommand<CreateBookingResult>;

internal sealed class CreateBookingValidator : AbstractValidator<CreateBookingCommand>
{
    public CreateBookingValidator()
    {
        RuleFor(c => c.DepartureId).NotEmpty();
        RuleFor(c => c.Type).IsInEnum();
        RuleFor(c => c.Guests).InclusiveBetween(1, Tour.MaxGuestsLimit);
        RuleFor(c => c.IdempotencyKey).NotEmpty().MaximumLength(64).Matches("^[A-Za-z0-9_-]+$")
            .WithMessage("Idempotency-Key must be 1-64 characters: letters, digits, '-' or '_' (a UUID works).");
    }
}

/// <summary>
/// Books seats or a whole departure. Safe against:
/// double clicks / client retries (Idempotency-Key, unique per customer, stored in the same transaction) and
/// customers racing for the last seats (row lock on the departure; its row version and the CHECK constraint are backstops).
/// </summary>
internal sealed class CreateBookingHandler(
    IUserContext user, ISlotRepository slots, ITourRepository tours, IAgencyRepository agencies, IBookingRepository bookings,
    IPaymentRepository payments, IUnitOfWork unitOfWork, BookingMapper mapper, TimeProvider time, IEmailConfirmationGate emailGate)
    : ICommandHandler<CreateBookingCommand, CreateBookingResult>
{
    public async Task<Result<CreateBookingResult>> Handle(CreateBookingCommand command, CancellationToken cancellationToken)
    {
        var confirmed = await emailGate.EnsureConfirmedAsync(user.UserId, cancellationToken);
        if (confirmed.IsFailure)
        {
            return confirmed.Error;
        }

        try
        {
            return await ConcurrencyRetry.RunAsync(unitOfWork, () => AttemptAsync(command, cancellationToken));
        }
        catch (DuplicateKeyException)
        {
            // The same request arrived twice at the same moment and the other one won: return its booking.
            unitOfWork.Reset();
            return await ReplayAsync(command, cancellationToken) ?? ConcurrencyRetry.Busy;
        }
    }

    private async Task<Result<CreateBookingResult>> AttemptAsync(CreateBookingCommand c, CancellationToken cancellationToken)
    {
        // Lock the departure row for the few milliseconds of the reservation: concurrent bookings of the same departure
        // queue here and each sees the seats left by the previous one. (Never held during payment: the booking is a hold.)
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var slot = await slots.GetByIdForUpdateAsync(c.DepartureId, cancellationToken);
        if (slot is null)
        {
            return BookingUseCaseErrors.DepartureNotFound;
        }

        // Checked under the lock, so a duplicate request that waited behind the original sees its booking.
        if (await ReplayAsync(c, cancellationToken) is { } replay)
        {
            return replay;
        }

        var tour = await tours.GetByIdAsync(slot.TourId, cancellationToken);
        if (tour is null)
        {
            return BookingUseCaseErrors.DepartureNotFound;
        }

        if (await agencies.GetByIdAsync(tour.AgencyId, cancellationToken) is not { IsActive: true })
        {
            return TourErrors.NotBookable;
        }

        var now = time.GetUtcNow();
        var booking = c.Type == BookingType.Private
            ? BookingService.BookPrivate(tour, slot, user.UserId, c.Guests, now, c.IdempotencyKey)
            : BookingService.BookSeats(tour, slot, user.UserId, c.Guests, now, c.IdempotencyKey);
        if (booking.IsFailure)
        {
            return booking.Error;
        }

        bookings.Add(booking.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CreateBookingResult(mapper.ToResponse(booking.Value, tour, now), Replayed: false);
    }

    private async Task<Result<CreateBookingResult>?> ReplayAsync(CreateBookingCommand c, CancellationToken cancellationToken)
    {
        var existing = await bookings.GetByIdempotencyKeyAsync(user.UserId, c.IdempotencyKey, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (!existing.MatchesRequest(c.DepartureId, c.Type, c.Guests))
        {
            return BookingUseCaseErrors.IdempotencyKeyReused;
        }

        var tour = (await tours.GetManyForDisplayAsync([existing.TourId], cancellationToken))[existing.TourId];
        var payment = (await payments.GetLatestForBookingsAsync([existing.Id], cancellationToken)).GetValueOrDefault(existing.Id);
        return new CreateBookingResult(mapper.ToResponse(existing, tour, time.GetUtcNow(), payment), Replayed: true);
    }
}
