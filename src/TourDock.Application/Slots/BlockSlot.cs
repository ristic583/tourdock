using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Application.Vehicles;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;

namespace TourDock.Application.Slots;

public sealed record BlockSlotCommand(Guid AgencyId, Guid SlotId) : IAgencyRequest, ICommand<SlotResponse>, IInvalidatesCatalog;

internal sealed class BlockSlotHandler(SlotEditor editor) : ICommandHandler<BlockSlotCommand, SlotResponse>
{
    public Task<Result<SlotResponse>> Handle(BlockSlotCommand command, CancellationToken cancellationToken) =>
        editor.EditAsync(command.AgencyId, command.SlotId, (slot, _) => Task.FromResult(slot.Block()), cancellationToken);
}
