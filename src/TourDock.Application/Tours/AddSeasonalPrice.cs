using FluentValidation;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Domain.Common;

namespace TourDock.Application.Tours;

public sealed record AddSeasonalPriceCommand(
    Guid AgencyId, Guid TourId, DateOnly From, DateOnly To, decimal? PricePerPerson, decimal? PrivatePrice)
    : IAgencyRequest, ICommand<TourResponse>, IInvalidatesCatalog;

internal sealed class AddSeasonalPriceValidator : AbstractValidator<AddSeasonalPriceCommand>
{
    public AddSeasonalPriceValidator()
    {
        RuleFor(c => c.To).GreaterThanOrEqualTo(c => c.From);
        RuleFor(c => c.PricePerPerson).ValidPrice();
        RuleFor(c => c.PrivatePrice).ValidPrice();
    }
}

internal sealed class AddSeasonalPriceHandler(TourEditor editor) : ICommandHandler<AddSeasonalPriceCommand, TourResponse>
{
    public Task<Result<TourResponse>> Handle(AddSeasonalPriceCommand command, CancellationToken cancellationToken) =>
        editor.EditAsync(command.AgencyId, command.TourId,
            (tour, _) => tour.AddSeasonalPrice(command.From, command.To, command.PricePerPerson.ToMoney(), command.PrivatePrice.ToMoney()),
                cancellationToken);
}
