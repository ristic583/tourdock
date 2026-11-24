using FluentValidation;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.TravelInfo;
using TourDock.Domain.Common;

namespace TourDock.Application.Catalog;

/// <param name="Currency">Optional display currency (ISO 4217); adds approximate converted prices to the response.</param>
public sealed record GetPublicTourQuery(Guid TourId, string? Currency = null) : IQuery<PublicTourResponse>;

internal sealed class GetPublicTourValidator : AbstractValidator<GetPublicTourQuery>
{
    public GetPublicTourValidator()
    {
        RuleFor(q => q.Currency!)
            .Must(DisplayCurrencies.IsAllowed)
            .When(q => q.Currency is not null)
            .WithMessage($"Currency must be EUR or one of: {string.Join(", ", DisplayCurrencies.Supported)}.");
    }
}

internal sealed class GetPublicTourHandler(
    ITourCatalog catalog, IExchangeRateService exchangeRates)
    : IQueryHandler<GetPublicTourQuery, PublicTourResponse>
{
    public async Task<Result<PublicTourResponse>> Handle(GetPublicTourQuery query, CancellationToken cancellationToken)
    {
        if (await catalog.GetTourAsync(query.TourId, cancellationToken) is not { } tour)
        {
            return CatalogErrors.TourNotFound;
        }

        if (query.Currency is null || query.Currency == Currencies.Eur)
        {
            return tour;
        }

        // The cached tour stays in EUR; conversion is applied per request. No rates = EUR only, never an error.
        var rates = await exchangeRates.GetLatestAsync(cancellationToken);
        return tour with
        {
            ConvertedPrices = CurrencyConverter.ConvertPrices(tour.PricePerPerson, tour.PrivatePrice, query.Currency,
            rates)
        };
    }
}
