using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Tours;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Bookings;

public sealed record CompleteFinishedBookingsCommand(int BatchSize = 200) : ICommand<int>;

internal sealed class CompleteFinishedBookingsHandler(IBookingRepository bookings, IUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<CompleteFinishedBookingsCommand, int>
{
    public async Task<Result<int>> Handle(CompleteFinishedBookingsCommand command, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var completed = 0;
        foreach (var id in await bookings.GetFinishedConfirmedIdsAsync(now, command.BatchSize, cancellationToken))
        {
            // One save per booking: a booking changed by someone else meanwhile is skipped (the next run retries it)
            // instead of failing the whole batch every run.
            unitOfWork.Reset();
            var booking = await bookings.GetByIdAsync(id, cancellationToken);
            if (booking is null || booking.Complete(now).IsFailure)
            {
                continue;
            }

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                completed++;
            }
            catch (ConcurrencyConflictException)
            {
                // Changed concurrently; picked up again on the next run.
            }
        }

        return completed;
    }
}
