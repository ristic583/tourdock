using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using TourDock.Application.Abstractions;

namespace TourDock.Infrastructure.TravelInfo;

public static class TravelInfoRegistration
{
    /// <summary>
    /// Exchange rates and weather clients. No readiness health checks on purpose: a third-party outage must not take
    /// our API out of rotation; these features degrade instead.
    /// </summary>
    public static IServiceCollection AddTravelInfo(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ExchangeRatesOptions>()
            .Bind(configuration.GetSection(ExchangeRatesOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<WeatherOptions>()
            .Bind(configuration.GetSection(WeatherOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHybridCache();

        services.AddHttpClient(TravelInfoHttpClients.ExchangeRates, (sp, http) =>
                http.BaseAddress = BaseAddress(sp.GetRequiredService<IOptions<ExchangeRatesOptions>>().Value.BaseUrl))
            .AddStandardResilienceHandler(ConfigureResilience);
        services.AddHttpClient(TravelInfoHttpClients.Weather, (sp, http) =>
                http.BaseAddress = BaseAddress(sp.GetRequiredService<IOptions<WeatherOptions>>().Value.BaseUrl))
            .AddStandardResilienceHandler(ConfigureResilience);

        services.AddSingleton<IExchangeRateService, FrankfurterExchangeRateService>();
        services.AddSingleton<IWeatherService, OpenMeteoWeatherService>();
        return services;
    }

    /// <summary>
    /// These calls only decorate a page, so they get a tight budget: a slow provider must not slow the page down.
    /// After repeated failures the circuit opens and calls fail fast (and fall back) for a while.
    /// </summary>
    private static void ConfigureResilience(HttpStandardResilienceOptions options)
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(8);
        options.Retry.MaxRetryAttempts = 2;
        options.Retry.Delay = TimeSpan.FromMilliseconds(300);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.MinimumThroughput = 5;
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
    }

    private static Uri BaseAddress(string url) => new(url.TrimEnd('/') + "/");
}
