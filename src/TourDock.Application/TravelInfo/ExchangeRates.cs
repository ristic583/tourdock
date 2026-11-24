using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Common;
using TourDock.Domain.Common;

namespace TourDock.Application.TravelInfo;

/// <summary>
/// Currencies travelers can see prices in. Display only: every booking, payment and refund stays in
/// <see cref="Currencies.Eur"/> (ADR 0006, ADR 0017).
/// </summary>
public static class DisplayCurrencies
{
    public static readonly IReadOnlyList<string> Supported = ["USD", "GBP", "CHF", "SEK", "NOK", "DKK", "PLN", "CZK", "HUF", "RON"];

    /// <summary>True for the platform currency or one of the <see cref="Supported"/> display currencies.</summary>
    public static bool IsAllowed(string code) => code == Currencies.Eur || Supported.Contains(code);
}

/// <param name="BaseCurrency">The currency prices are charged in; every rate converts from it.</param>
/// <param name="Available">False when the rate provider is down and nothing was cached: show base prices only.</param>
/// <param name="Date">ECB publication date of the rates.</param>
/// <param name="IsStale">The provider is unreachable; these are the last rates fetched.</param>
/// <param name="Rates">Units of each currency per 1 <paramref name="BaseCurrency"/>.</param>
public sealed record ExchangeRatesResponse(
    string BaseCurrency,
    bool Available,
    DateOnly? Date,
    bool IsStale,
    IReadOnlyDictionary<string, decimal> Rates);

/// <summary>A tour's prices converted for display. Approximate: the traveler is charged the EUR price.</summary>
public sealed record ConvertedPrices(
    string Currency,
    decimal Rate,
    DateOnly RatesDate,
    bool IsStale,
    MoneyDto? PricePerPerson,
    MoneyDto? PrivatePrice);

public sealed record GetExchangeRatesQuery : IQuery<ExchangeRatesResponse>;

internal sealed class GetExchangeRatesHandler(IExchangeRateService rates) : IQueryHandler<GetExchangeRatesQuery, ExchangeRatesResponse>
{
    public async Task<Result<ExchangeRatesResponse>> Handle(GetExchangeRatesQuery query, CancellationToken cancellationToken)
    {
        var snapshot = await rates.GetLatestAsync(cancellationToken);
        if (snapshot is null)
        {
            return new ExchangeRatesResponse(Currencies.Eur, Available: false, Date: null, IsStale: false, Rates: new Dictionary<string, decimal>());
        }

        var supported = snapshot.Rates
            .Where(rate => DisplayCurrencies.Supported.Contains(rate.Key))
            .ToDictionary(rate => rate.Key, rate => rate.Value);
        return new ExchangeRatesResponse(snapshot.BaseCurrency, Available: true, snapshot.Date, snapshot.IsStale, supported);
    }
}

public static class CurrencyConverter
{
    /// <summary>Converts a base-currency amount, rounded to 2 decimals the same way <see cref="Money"/> rounds.</summary>
    public static MoneyDto? Convert(MoneyDto? amount, string currency, decimal rate) =>
        amount is null ? null : new MoneyDto(Math.Round(amount.Amount * rate, 2, MidpointRounding.AwayFromZero), currency);

    /// <summary>Null when the currency is the base one or no rate for it is available (the UI then shows EUR only).</summary>
    public static ConvertedPrices? ConvertPrices(MoneyDto? pricePerPerson, MoneyDto? privatePrice, string? currency, ExchangeRatesSnapshot? rates)
    {
        if (currency is null || rates is null || currency == rates.BaseCurrency || !rates.Rates.TryGetValue(currency, out var rate) || rate <= 0)
        {
            return null;
        }

        return new ConvertedPrices(currency, rate, rates.Date, rates.IsStale,
            Convert(pricePerPerson, currency, rate), Convert(privatePrice, currency, rate));
    }
}
