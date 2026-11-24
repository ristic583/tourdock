using Anthropic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TourDock.Application.Abstractions;
using TourDock.Infrastructure.Jobs;

namespace TourDock.Infrastructure.Assistant;

public static class AssistantRegistration
{
    /// <summary>
    /// The travel assistant. No readiness check on purpose: without the model provider the chat is hidden and the rest
    /// of the site works, so an outage there must not take the API out of rotation.
    /// </summary>
    public static IServiceCollection AddAssistant(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AssistantOptions>()
            .Bind(configuration.GetSection(AssistantOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<AssistantOptions>>().Value;
            return new AssistantSettings(
                !string.IsNullOrWhiteSpace(o.ApiKey), o.MaxTurnsPerConversation, o.MaxMessagesPerDay, o.Retention, o.ExposeUsage);
        });

        // Built only when the assistant is used, so a missing key never reaches the SDK's own credential lookup.
        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<AssistantOptions>>().Value;
            return new AnthropicClient { ApiKey = o.ApiKey, Timeout = o.CallTimeout, MaxRetries = 1 };
        });

        services.AddScoped<ITravelAssistant, ClaudeTravelAssistant>();
        services.AddScoped<IAssistantConversations, AssistantConversations>();
        services.AddSingleton<AssistantRetentionJob>();
        services.AddHostedService(sp => sp.GetRequiredService<AssistantRetentionJob>());
        return services;
    }
}
