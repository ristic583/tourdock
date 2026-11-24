using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TourDock.Application.Abstractions;
using TourDock.Application.TravelInfo;
using TourDock.Domain.Common;

namespace TourDock.Infrastructure.TravelInfo;

/// <summary>
/// EUR reference rates from Frankfurter. Two cache entries: a short "fresh" one that limits calls to the provider
/// (with stampede protection from <see cref="HybridCache"/>), and a long "last good" one served, marked stale,
/// while the provider is down. Timeouts, retries and the circuit breaker sit in the HTTP client pipeline.
/// </summary>
internal sealed partial class FrankfurterExchangeRateService(
    IHttpClientFactory httpClients,
    HybridCache cache,
    IOptions<ExchangeRatesOptions> options,
    ILogger<FrankfurterExchangeRateService> logger) : IExchangeRateService
{
    private const string FreshKey = "travel-info:fx:fresh:EUR";
    private const string LastGoodKey = "travel-info:fx:last-good:EUR";

    public async Task<ExchangeRatesSnapshot?> GetLatestAsync(CancellationToken cancellationToken)
    {
        try
        {
            var rates = await cache.GetOrCreateAsync(FreshKey, FetchAndRememberAsync, Expiring(options.Value.CacheDuration), cancellationToken: cancellationToken);
            return ToSnapshot(rates, isStale: false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogProviderFailed(ex);
            var lastGood = await cache.GetOrCreateAsync<CachedRates?>(
                LastGoodKey, _ => ValueTask.FromResult<CachedRates?>(null), ReadOnly, cancellationToken: cancellationToken);
            return lastGood is null ? null : ToSnapshot(lastGood, isStale: true);
        }
    }

    private async ValueTask<CachedRates> FetchAndRememberAsync(CancellationToken cancellationToken)
    {
        var rates = await FetchAsync(cancellationToken);
        await cache.SetAsync(LastGoodKey, rates, Expiring(options.Value.StaleFallbackDuration), cancellationToken: cancellationToken);
        return rates;
    }

    private async Task<CachedRates> FetchAsync(CancellationToken cancellationToken)
    {
        var http = httpClients.CreateClient(TravelInfoHttpClients.ExchangeRates);
        var symbols = string.Join(',', DisplayCurrencies.Supported);
        var response = await http.GetFromJsonAsync<FrankfurterLatest>($"latest?base={Currencies.Eur}&symbols={symbols}", cancellationToken)
            ?? throw new InvalidDataException("Empty response from the exchange rate provider.");

        // Never trust a third party blindly: a wrong base or a zero rate would show nonsense prices.
        if (response.Base != Currencies.Eur || response.Rates is null || response.Rates.Count == 0 || response.Rates.Values.Any(rate => rate <= 0))
        {
            throw new InvalidDataException("Unexpected exchange rate response.");
        }

        return new CachedRates(response.Date, new Dictionary<string, decimal>(response.Rates));
    }

    private static ExchangeRatesSnapshot ToSnapshot(CachedRates rates, bool isStale) =>
        new(Currencies.Eur, rates.Date, rates.Rates, isStale);

    private static HybridCacheEntryOptions Expiring(TimeSpan duration) => new() { Expiration = duration, LocalCacheExpiration = duration };

    private static readonly HybridCacheEntryOptions ReadOnly = new()
    {
        Flags = HybridCacheEntryFlags.DisableLocalCacheWrite | HybridCacheEntryFlags.DisableDistributedCacheWrite,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Exchange rate provider failed; serving last known rates if any")]
    private partial void LogProviderFailed(Exception exception);

    internal sealed record CachedRates(DateOnly Date, Dictionary<string, decimal> Rates);

    private sealed record FrankfurterLatest(string Base, DateOnly Date, Dictionary<string, decimal>? Rates);
}
