using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Payments;
using TourDock.Application.Agencies;
using TourDock.Application.Catalog;
using TourDock.Infrastructure.Assistant;
using TourDock.Infrastructure.Embeddings;
using TourDock.Infrastructure.Identity;
using TourDock.Infrastructure.OAuth;
using TourDock.Infrastructure.Persistence;
using TourDock.Infrastructure.Repositories;
using TourDock.Infrastructure.TravelInfo;

namespace TourDock.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseConnectionStringName = "Database";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddLocalSecretsGuard(configuration);
        services.AddPersistence(configuration);
        services.AddAuth(configuration);
        services.AddPayments(configuration);
        services.AddFiles(configuration);
        services.AddTravelInfo(configuration);
        services.AddEmbeddings(configuration);
        services.AddAssistant(configuration);
        services.AddOAuthServer(configuration);
        services.AddEmail(configuration);
        return services;
    }

    private static void AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(DatabaseConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{DatabaseConnectionStringName}' is not configured. " +
                $"Set the 'ConnectionStrings__{DatabaseConnectionStringName}' environment variable.");
        }

        services.AddSingleton<AuditingInterceptor>();
        services.AddSingleton<Outbox.DomainEventsInterceptor>();
        services.AddNpgsqlDataSource(connectionString);
        services.AddDbContext<TourDockDbContext>((serviceProvider, options) => options
            .UseNpgsql(serviceProvider.GetRequiredService<NpgsqlDataSource>())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                serviceProvider.GetRequiredService<AuditingInterceptor>(),
                serviceProvider.GetRequiredService<Outbox.DomainEventsInterceptor>()));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TourDockDbContext>());
        services.AddScoped<IAgencyRepository, AgencyRepository>();
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<ITourRepository, TourRepository>();
        services.AddScoped<ISlotRepository, SlotRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IReviewRepository, Reviews.ReviewRepository>();
        services.AddScoped<IAdminAuditLog, Reviews.AdminAuditLog>();
        services.AddScoped<Application.Reviews.IReviewReader, Reviews.ReviewReader>();
        services.AddHostedService<Jobs.SlotGenerationJob>();
        services.AddHostedService<Jobs.BookingMaintenanceJob>();

        // In-memory L1 cache today; adding a distributed cache (e.g. Redis) later turns on L2 with no code changes.
        services.AddHybridCache();
        services.AddScoped<IAgencyStatsReader, Agencies.AgencyStatsReader>();
        services.AddScoped<ICityDirectory, Places.CityDirectory>();
        services.AddScoped<Catalog.TourCatalog>();
        services.AddScoped<ITourCatalog>(sp => sp.GetRequiredService<Catalog.TourCatalog>());
        services.AddScoped<ICatalogCache>(sp => sp.GetRequiredService<Catalog.TourCatalog>());

        services.AddHealthChecks()
            .AddNpgSql(name: "postgres", tags: [HealthCheckTags.Ready])
            .AddCheck<Outbox.OutboxHealthCheck>("outbox", tags: [HealthCheckTags.Ready])
            .AddCheck<Payments.RefundHealthCheck>("refunds", tags: [HealthCheckTags.Ready]);
    }

    private static void AddPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<Payments.PaymentsOptions>()
            .Bind(configuration.GetSection(Payments.PaymentsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IPaymentProvider, Payments.HttpPaymentProvider>((sp, http) =>
            {
                var options = sp.GetRequiredService<IOptions<Payments.PaymentsOptions>>().Value;
                http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);
            })
            // Timeouts, retries with jitter and a circuit breaker. Retrying POSTs is safe: every call has an Idempotency-Key.
            .AddStandardResilienceHandler();

        services.AddSingleton<IWebhookSignatureVerifier, Payments.WebhookSignatureVerifier>();
        services.AddSingleton<IReturnUrlPolicy, Payments.ReturnUrlPolicy>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IRefundRepository, RefundRepository>();
        services.AddScoped<IProcessedEventStore, Outbox.ProcessedEventStore>();
        services.AddScoped<IOutbox, Outbox.EfOutbox>();
        services.AddScoped<ISentNotificationLog, Outbox.SentNotificationLog>();
        services.AddSingleton<Outbox.OutboxProcessor>();
        services.AddHostedService(sp => sp.GetRequiredService<Outbox.OutboxProcessor>());
    }

    private static void AddEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<Email.EmailOptions>()
            .Bind(configuration.GetSection(Email.EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IEmailSender, Email.SmtpEmailSender>();
        services.AddSingleton(sp => new NotificationSettings(sp.GetRequiredService<IOptions<Email.EmailOptions>>().Value.WebBaseUrl));
    }

    private static void AddFiles(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<Files.StorageOptions>()
            .Bind(configuration.GetSection(Files.StorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(sp => Files.S3FileStorage.CreateClient(sp.GetRequiredService<IOptions<Files.StorageOptions>>().Value));
        services.AddSingleton<IFileStorage, Files.S3FileStorage>();
        services.AddSingleton<IPhotoUrls, Files.PhotoUrls>();
        services.AddSingleton<IImageProcessor, Files.SkiaImageProcessor>();
        services.AddHostedService<Files.StorageInitializer>();
    }

    private static void AddAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.Configure<AdminSeedOptions>(configuration.GetSection(AdminSeedOptions.SectionName));

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                // NIST SP 800-63B: length over composition rules.
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                // Backstop for guessing from many addresses at once. The everyday limit is LoginThrottle (per email and
                // address), so failures from a stranger's address can't lock the owner out.
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 50;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<TourDockDbContext>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = TokenService.SigningKey(jwt.Value),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = "name",
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Admin, policy => policy.RequireRole(Roles.Admin));

        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext, UserContext>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddGoogleSignIn(configuration);
        services.Configure<AccountOptions>(configuration.GetSection(AccountOptions.SectionName));
        services.AddScoped<IEmailConfirmationGate, EmailConfirmationGate>();
        services.AddMemoryCache();
        services.AddSingleton<ILoginThrottle, LoginThrottle>();

        // Breached-password check on sign-up: a tight budget, it must never make registering slow.
        services.AddOptions<PasswordScreenOptions>().Bind(configuration.GetSection(PasswordScreenOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddHttpClient(PasswordScreenHttpClient.Name, (sp, http) =>
                http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<PasswordScreenOptions>>().Value.BreachListUrl.TrimEnd('/') + "/"))
            .AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(4);
                o.Retry.MaxRetryAttempts = 1;
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                o.CircuitBreaker.MinimumThroughput = 5;
                o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(60);
            });
        services.AddSingleton<IPasswordScreen, PwnedPasswordScreen>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddHostedService<AdminSeeder>();
        services.AddHostedService<Seeding.DemoDataSeeder>();
    }
}

public static class AuthPolicies
{
    public const string Admin = "Admin";
}
