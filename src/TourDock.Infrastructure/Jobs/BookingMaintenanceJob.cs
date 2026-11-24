using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Bookings;

namespace TourDock.Infrastructure.Jobs;

/// <summary>
/// Every minute: releases seats of unpaid bookings whose 15-minute hold ran out, and marks finished trips as completed
/// (which unlocks reviews). Safe to run on several instances: each change is checked and saved with optimistic concurrency.
/// </summary>
internal sealed partial class BookingMaintenanceJob(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<BookingMaintenanceJob> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Jobs:BookingMaintenance:Enabled", true))
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(configuration.GetValue("Jobs:BookingMaintenance:IntervalSeconds", 60)));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var expire = scope.ServiceProvider.GetRequiredService<ICommandHandler<ExpireHoldsCommand, int>>();
                var complete = scope.ServiceProvider.GetRequiredService<ICommandHandler<CompleteFinishedBookingsCommand, int>>();
                var expired = await expire.Handle(new ExpireHoldsCommand(), stoppingToken);
                var completed = await complete.Handle(new CompleteFinishedBookingsCommand(), stoppingToken);
                if (expired.Value + completed.Value > 0)
                {
                    LogRun(logger, expired.Value, completed.Value);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Bookings: {Expired} holds expired, {Completed} completed")]
    private static partial void LogRun(ILogger logger, int expired, int completed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Booking maintenance failed; will retry on the next run")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
