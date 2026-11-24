using FluentValidation;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Domain.Common;

namespace TourDock.Application.Tours;

public sealed record DeleteTourCommand(Guid AgencyId, Guid TourId) : IAgencyRequest, ICommand<Unit>, IInvalidatesCatalog;

internal sealed class DeleteTourHandler(
    IAgencyAccess access, ITourRepository tours, IBookingRepository bookings, IUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<DeleteTourCommand, Unit>
{
    public async Task<Result<Unit>> Handle(DeleteTourCommand command, CancellationToken cancellationToken)
    {
        var agency = await access.RequireAsync(command.AgencyId, AgencyAccessLevel.Manage, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        var tour = await tours.GetAsync(command.AgencyId, command.TourId, cancellationToken);
        if (tour is null)
        {
            return TourUseCaseErrors.NotFound;
        }

        var now = time.GetUtcNow();
        if (await bookings.HasUpcomingActiveForTourAsync(tour.Id, now, cancellationToken))
        {
            return TourUseCaseErrors.HasUpcomingBookings;
        }

        foreach (var rule in tour.ScheduleRules.Where(r => r.IsActive).ToList())
        {
            tour.DeactivateScheduleRule(rule.Id);
        }

        tour.Delete(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
