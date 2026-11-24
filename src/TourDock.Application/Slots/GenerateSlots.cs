using FluentValidation;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Vehicles;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;

namespace TourDock.Application.Slots;

/// <summary>System command (no user): generates departures for all active rules up to the horizon.</summary>
public sealed record GenerateSlotsCommand : ICommand<GenerateSlotsResult>, IInvalidatesCatalog;

internal sealed class GenerateSlotsHandler(ITourRepository tours, SlotScheduler scheduler, IUnitOfWork unitOfWork)
    : ICommandHandler<GenerateSlotsCommand, GenerateSlotsResult>
{
    public async Task<Result<GenerateSlotsResult>> Handle(GenerateSlotsCommand command, CancellationToken cancellationToken)
    {
        int created = 0, skipped = 0;
        foreach (var tour in await tours.ListWithActiveRulesAsync(cancellationToken))
        {
            foreach (var rule in tour.ScheduleRules.Where(r => r.IsActive))
            {
                var result = await scheduler.GenerateAsync(tour, rule, cancellationToken);
                created += result.DeparturesCreated;
                skipped += result.DeparturesSkippedForConflicts;
            }

            // Save per tour: keeps transactions small and one bad tour can't block the others.
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new GenerateSlotsResult(created, skipped);
    }
}
