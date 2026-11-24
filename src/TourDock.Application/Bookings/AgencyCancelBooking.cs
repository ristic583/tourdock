using FluentValidation;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Common;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Bookings;

public sealed record AgencyCancelBookingCommand(Guid AgencyId, Guid BookingId, string Reason) : IAgencyRequest,
    ICommand<CancellationResult>;

internal sealed class AgencyCancelBookingValidator : AbstractValidator<AgencyCancelBookingCommand>
{
    public AgencyCancelBookingValidator() => RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class AgencyCancelBookingHandler(IAgencyAccess access, BookingChanger changer)
    : ICommandHandler<AgencyCancelBookingCommand, CancellationResult>
{
    public async Task<Result<CancellationResult>> Handle(AgencyCancelBookingCommand command, CancellationToken cancellationToken)
    {
        var agency = await access.RequireAsync(command.AgencyId, AgencyAccessLevel.Manage, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        return await changer.ChangeAsync(command.BookingId, b => b.AgencyId == command.AgencyId,
            (booking, slot, now) => BookingService.Cancel(booking, slot, CancelledBy.Agency, command.Reason, now), cancellationToken);
    }
}
