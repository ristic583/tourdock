using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Slots;

namespace TourDock.Infrastructure.Jobs;

/// <summary>
/// Keeps departures generated a year ahead (BookingHorizon) for every active schedule rule.
/// Idempotent (unique index on rule + start time), so running it on several instances or twice is harmless.
/// </summary>
internal sealed partial class SlotGenerationJob(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<SlotGenerationJob> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Jobs:SlotGeneration:Enabled", true))
        {
            return;
        }

        var interval = TimeSpan.FromHours(configuration.GetValue("Jobs:SlotGeneration:IntervalHours", 6));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<GenerateSlotsCommand, GenerateSlotsResult>>();
                var result = await handler.Handle(new GenerateSlotsCommand(), stoppingToken);
                if (result.IsSuccess)
                {
                    LogGenerated(logger, result.Value.DeparturesCreated, result.Value.DeparturesSkippedForConflicts);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Slot generation: {Created} departures created, {Skipped} skipped for vehicle conflicts")]
    private static partial void LogGenerated(ILogger logger, int created, int skipped);

    [LoggerMessage(Level = LogLevel.Error, Message = "Slot generation failed; will retry on the next run")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
