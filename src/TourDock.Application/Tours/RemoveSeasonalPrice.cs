using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Domain.Common;

namespace TourDock.Application.Tours;

public sealed record RemoveSeasonalPriceCommand(
    Guid AgencyId, Guid TourId, Guid SeasonalPriceId)
    : IAgencyRequest, ICommand<TourResponse>, IInvalidatesCatalog;

internal sealed class RemoveSeasonalPriceHandler(TourEditor editor) : ICommandHandler<RemoveSeasonalPriceCommand, TourResponse>
{
    public Task<Result<TourResponse>> Handle(RemoveSeasonalPriceCommand command, CancellationToken cancellationToken) =>
        editor.EditAsync(command.AgencyId, command.TourId, (tour, _) => tour.RemoveSeasonalPrice(command.SeasonalPriceId),
            cancellationToken);
}
