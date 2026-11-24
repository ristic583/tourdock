using FluentValidation;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Application.Vehicles;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;

namespace TourDock.Application.Slots;

public sealed record CancelSlotCommand(Guid AgencyId, Guid SlotId, string Reason) : IAgencyRequest, ICommand<SlotResponse>,
    IInvalidatesCatalog;

internal sealed class CancelSlotValidator : AbstractValidator<CancelSlotCommand>
{
    public CancelSlotValidator() => RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);
}

/// <summary>Agency cancels a departure: every active booking on it is cancelled with a full refund, in the same transaction.</summary>
internal sealed class CancelSlotHandler(SlotEditor editor, IBookingRepository bookings, Payments.RefundScheduler refundScheduler)
    : ICommandHandler<CancelSlotCommand, SlotResponse>
{
    public Task<Result<SlotResponse>> Handle(CancelSlotCommand command, CancellationToken cancellationToken) =>
        editor.EditAsync(command.AgencyId, command.SlotId, async (slot, now) =>
        {
            var active = await bookings.GetActiveForSlotAsync(slot.Id, cancellationToken);
            var cancelled = slot.Cancel(now);
            if (cancelled.IsFailure)
            {
                return cancelled;
            }

            foreach (var booking in active)
            {
                var result = BookingService.Cancel(booking, slot, CancelledBy.Agency, command.Reason, now);
                if (result.IsFailure)
                {
                    return result.Error;
                }

                await refundScheduler.ScheduleForBookingAsync(booking, result.Value, "Departure cancelled by the agency",
                    cancellationToken);
            }

            return Result.Success();
        }, cancellationToken);
}
