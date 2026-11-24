using FluentValidation;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Tours;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Bookings;

public sealed record CancelBookingCommand(Guid BookingId, string? Reason) : ICommand<CancellationResult>;

internal sealed class CancelBookingValidator : AbstractValidator<CancelBookingCommand>
{
    public CancelBookingValidator() => RuleFor(c => c.Reason).MaximumLength(500);
}

internal sealed class CancelBookingHandler(
    IUserContext user, BookingChanger changer)
    : ICommandHandler<CancelBookingCommand, CancellationResult>
{
    public Task<Result<CancellationResult>> Handle(CancelBookingCommand command, CancellationToken cancellationToken) =>
        changer.ChangeAsync(command.BookingId, b => b.CustomerId == user.UserId,
            (booking, slot, now) => BookingService.Cancel(booking, slot, CancelledBy.Customer, command.Reason, now), cancellationToken);
}
