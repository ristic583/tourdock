using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Behaviors;
using TourDock.Application.Common;
using TourDock.Application.Slots;
using TourDock.Application.Tours;

namespace TourDock.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.Scan(scan => scan.FromAssemblies(assembly)
            .AddClasses(c => c.AssignableToAny(typeof(ICommandHandler<,>), typeof(IQueryHandler<,>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAgencyAccess, AgencyAccess>();
        services.AddScoped<TourEditor>();
        services.AddScoped<Assistant.IAgentToolGroup, Assistant.CatalogTools>();
        services.AddScoped<Assistant.IAgentToolGroup, Assistant.TravelerTools>();
        services.AddScoped<Assistant.IAgentToolGroup, Assistant.AgencyTools>();
        services.AddScoped<Assistant.IAgentTools, Assistant.AgentTools>();
        services.AddScoped<SlotEditor>();
        services.AddScoped<SlotScheduler>();
        services.AddScoped<Bookings.BookingChanger>();
        services.AddSingleton<Bookings.BookingMapper>();
        services.AddScoped<Payments.RefundScheduler>();
        services.AddScoped<AdminAudit>();
        // IMeterFactory comes from the host (registered by default in ASP.NET Core and the generic host).
        services.AddSingleton<Observability.TourDockMetrics>();
        services.AddScoped<Reviews.ReviewModeration>();
        services.AddSingleton<Notifications.IDomainEventOutboxMapper, Notifications.NotificationEventMapper>();
        services.Scan(scan => scan.FromAssemblies(assembly)
            .AddClasses(c => c.AssignableTo(typeof(IOutboxMessageHandler<>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        // Registered inner-first: the last decorator applied is the outermost one.
        // Commands: Logging -> Validation -> CatalogInvalidation -> Handler.
        services.Decorate(typeof(ICommandHandler<,>), typeof(CatalogInvalidationDecorator<,>));
        services.Decorate(typeof(ICommandHandler<,>), typeof(ValidationCommandDecorator<,>));
        services.Decorate(typeof(ICommandHandler<,>), typeof(AgencyAccessCommandDecorator<,>));
        services.Decorate(typeof(ICommandHandler<,>), typeof(LoggingCommandDecorator<,>));
        services.Decorate(typeof(IQueryHandler<,>), typeof(ValidationQueryDecorator<,>));
        services.Decorate(typeof(IQueryHandler<,>), typeof(AgencyAccessQueryDecorator<,>));
        services.Decorate(typeof(IQueryHandler<,>), typeof(LoggingQueryDecorator<,>));

        return services;
    }
}
