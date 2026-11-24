using TourDock.Application.Abstractions.Data;
using TourDock.Domain.Common;

namespace TourDock.Application.Common;

/// <summary>
/// Runs a load → decide → save operation, retrying from fresh data when another request changed the same rows first.
/// With optimistic concurrency, the loser of a race re-reads the departure and either succeeds or gets a clear
/// business error (e.g. "not enough seats") instead of a generic conflict.
/// </summary>
internal static class ConcurrencyRetry
{
    public const int MaxAttempts = 3;

    public static readonly Error Busy =
        Error.Conflict("Concurrency.Busy", "This departure is being booked by many people right now. Please try again.");

    public static async Task<Result<T>> RunAsync<T>(IUnitOfWork unitOfWork, Func<Task<Result<T>>> attempt)
    {
        for (var i = 1; i <= MaxAttempts; i++)
        {
            unitOfWork.Reset();
            try
            {
                return await attempt();
            }
            catch (ConcurrencyConflictException) when (i < MaxAttempts)
            {
                // Someone else won the race; retry with the current state.
            }
            catch (ConcurrencyConflictException)
            {
                return Busy;
            }
        }

        return Busy;
    }
}
