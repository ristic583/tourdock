using FluentValidation;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Tours;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Bookings;

public sealed record ReduceGuestsCommand(Guid BookingId, int Guests) : ICommand<CancellationResult>;

internal sealed class ReduceGuestsValidator : AbstractValidator<ReduceGuestsCommand>
{
    public ReduceGuestsValidator() => RuleFor(c => c.Guests).GreaterThanOrEqualTo(1);
}

internal sealed class ReduceGuestsHandler(
    IUserContext user, BookingChanger changer)
    : ICommandHandler<ReduceGuestsCommand, CancellationResult>
{
    public Task<Result<CancellationResult>> Handle(ReduceGuestsCommand command, CancellationToken cancellationToken) =>
        changer.ChangeAsync(command.BookingId, b => b.CustomerId == user.UserId,
            (booking, slot, now) => BookingService.ReduceGuests(booking, slot, command.Guests, now), cancellationToken);
}
