using FluentValidation;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Tours;

public sealed record UpdateTourPricingCommand(
    Guid AgencyId, Guid TourId, BookingMode BookingMode, decimal? PricePerPerson, decimal? PrivatePrice)
    : IAgencyRequest, ICommand<TourResponse>, IInvalidatesCatalog;

internal sealed class UpdateTourPricingValidator : AbstractValidator<UpdateTourPricingCommand>
{
    public UpdateTourPricingValidator()
    {
        RuleFor(c => c.BookingMode).IsInEnum();
        RuleFor(c => c.PricePerPerson).ValidPrice();
        RuleFor(c => c.PrivatePrice).ValidPrice();
    }
}

internal sealed class UpdateTourPricingHandler(TourEditor editor) : ICommandHandler<UpdateTourPricingCommand, TourResponse>
{
    public Task<Result<TourResponse>> Handle(UpdateTourPricingCommand command, CancellationToken cancellationToken) =>
        editor.EditAsync(command.AgencyId, command.TourId,
            (tour, _) => tour.UpdatePricing(command.BookingMode, command.PricePerPerson.ToMoney(), command.PrivatePrice.ToMoney()),
                cancellationToken);
}
