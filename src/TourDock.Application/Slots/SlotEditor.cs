using TourDock.Application.Abstractions.Data;
using TourDock.Application.Common;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Slots;

namespace TourDock.Application.Slots;

internal sealed class SlotEditor(IAgencyAccess access, ISlotRepository slots, IUnitOfWork unitOfWork, TimeProvider time)
{
    public async Task<Result<SlotResponse>> EditAsync(
        Guid agencyId, Guid slotId, Func<Slot, DateTimeOffset, Task<Result>> change, CancellationToken cancellationToken)
    {
        var agency = await access.RequireAsync(agencyId, AgencyAccessLevel.Manage, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        var slot = await slots.GetAsync(agencyId, slotId, cancellationToken);
        if (slot is null)
        {
            return SlotUseCaseErrors.NotFound;
        }

        var result = await change(slot, time.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return slot.ToResponse();
    }
}
