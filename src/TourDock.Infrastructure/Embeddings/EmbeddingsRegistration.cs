using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using TourDock.Application.Abstractions;
using TourDock.Infrastructure.Jobs;

namespace TourDock.Infrastructure.Embeddings;

public static class EmbeddingsRegistration
{
    /// <summary>
    /// Voyage client, cached query vectors and the sync job. No readiness check on purpose: without the provider, search
    /// keeps working in its normal order, so a third-party outage must not take the API out of rotation.
    /// </summary>
    public static IServiceCollection AddEmbeddings(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<VoyageOptions>()
            .Bind(configuration.GetSection(VoyageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHybridCache();
        services.AddHttpClient(EmbeddingHttpClients.Voyage, (sp, http) =>
                http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<VoyageOptions>>().Value.BaseUrl.TrimEnd('/') + "/"))
            .AddStandardResilienceHandler(ConfigureResilience);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<EmbeddingTokenBudget>();
        services.AddSingleton<IEmbeddingProvider, VoyageEmbeddingProvider>();
        services.TryAddSingleton<ISemanticSearchLimiter, UnlimitedSemanticSearch>(); // the API registers a per-IP one
        services.AddSingleton<IQueryEmbeddings, QueryEmbeddings>();
        services.AddSingleton<TourEmbeddingSyncJob>();
        services.AddHostedService(sp => sp.GetRequiredService<TourEmbeddingSyncJob>());
        return services;
    }

    /// <summary>
    /// Batches of tour texts take a few seconds; searches don't wait for this budget (they have their own short timeout
    /// in <see cref="QueryEmbeddings"/>). After repeated failures the circuit opens and calls fail fast for a while.
    /// </summary>
    private static void ConfigureResilience(HttpStandardResilienceOptions options)
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(15);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(40);
        options.Retry.MaxRetryAttempts = 2;
        options.Retry.Delay = TimeSpan.FromSeconds(1);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
        options.CircuitBreaker.MinimumThroughput = 5;
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
    }
}
