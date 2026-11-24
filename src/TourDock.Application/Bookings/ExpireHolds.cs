using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Tours;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Bookings;

/// <summary>System commands run by the booking maintenance job.</summary>
public sealed record ExpireHoldsCommand(int BatchSize = 100) : ICommand<int>;

internal sealed class ExpireHoldsHandler(
    IBookingRepository bookings, BookingChanger changer, TimeProvider time)
    : ICommandHandler<ExpireHoldsCommand, int>
{
    public async Task<Result<int>> Handle(ExpireHoldsCommand command, CancellationToken cancellationToken)
    {
        var expired = 0;
        foreach (var id in await bookings.GetExpiredHoldIdsAsync(time.GetUtcNow(), command.BatchSize, cancellationToken))
        {
            var result = await changer.ChangeAsync(id, _ => true,
                (booking, slot, now) =>
                {
                    var r = BookingService.Expire(booking, slot, now);
                    return r.IsSuccess ? Money.Zero() : r.Error!;
                }, cancellationToken);
            expired += result.IsSuccess ? 1 : 0;
        }

        return expired;
    }
}
