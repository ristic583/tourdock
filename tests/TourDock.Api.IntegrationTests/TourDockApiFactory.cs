using System.Net;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using TourDock.Api.IntegrationTests.TravelInfo;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Payments;
using TourDock.Infrastructure.Identity;
using TourDock.Infrastructure.Persistence;
using TourDock.Infrastructure.TravelInfo;

namespace TourDock.Api.IntegrationTests;

/// <summary>
/// Boots the real API in memory against a real PostgreSQL instance (with all migrations applied)
/// running in a throwaway container.
/// </summary>
public class TourDockApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // The same database image as compose.yaml (PostgreSQL + pgvector), built once per test run from deploy/db/Dockerfile.
    // Docker's layer cache makes later builds instant.
    private static readonly IFutureDockerImage DatabaseImage = new ImageFromDockerfileBuilder()
        .WithDockerfileDirectory(Path.Combine(RepositoryRoot(), "deploy", "db"))
        .WithDockerfile("Dockerfile")
        .WithName("tourdock-db:integration-tests")
        .WithDeleteIfExists(false)
        .WithCleanUp(false)
        .Build();

    private static readonly Lazy<Task> DatabaseImageBuilt = new(() => DatabaseImage.CreateAsync());

    private PostgreSqlContainer? _database;

    // Same object storage as compose.yaml, so uploads are tested against a real S3-compatible server.
    private readonly IContainer _storage = new ContainerBuilder("rustfs/rustfs:1.0.4")
        .WithEnvironment("RUSTFS_ACCESS_KEY", "testaccesskey")
        .WithEnvironment("RUSTFS_SECRET_KEY", "testsecretkey123")
        .WithPortBinding(9000, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9000).ForPath("/").ForStatusCodeMatching(_ => true)))
        .Build();

    public string StorageUrl => $"http://{_storage.Hostname}:{_storage.GetMappedPublicPort(9000)}";

    public const string WebhookSecret = "integration-tests-webhook-secret-0123456789";

    public const string AdminEmail = "admin@example.com";

    /// <summary>The only origin payment return URLs may point to in tests.</summary>
    public const string WebOrigin = "https://app.example.com";

    public FakePaymentProvider PaymentProvider { get; } = new();

    /// <summary>Stands in for Voyage AI (semantic search); tests never call the real provider.</summary>
    public FakeEmbeddingProvider Embeddings { get; } = new();

    /// <summary>Stands in for Claude (the travel assistant).</summary>
    public FakeTravelAssistant Assistant { get; } = new();

    /// <summary>Stands in for Frankfurter; answers 503 until a test says otherwise. Tests never call real third parties.</summary>
    public StubHttpHandler ExchangeRatesHttp { get; } = new();

    /// <summary>Stands in for Open-Meteo; answers 503 until a test says otherwise.</summary>
    public StubHttpHandler WeatherHttp { get; } = new();

    /// <summary>Stands in for the Pwned Passwords range API; answers 503 (the check fails open) until a test says otherwise.</summary>
    public StubHttpHandler PasswordsHttp { get; } = new();

    /// <summary>Every log entry the API writes, for checks such as "no personal data in logs".</summary>
    public LogCapture Logs { get; } = new();

    /// <summary>Captures notification emails instead of sending them.</summary>
    public FakeEmailSender Emails { get; } = new();
    public const string AdminPassword = "admin-password-for-tests";

    public string ConnectionString =>
        (_database ?? throw new InvalidOperationException("The database container hasn't started yet.")).GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await DatabaseImageBuilt.Value;
        _database = new PostgreSqlBuilder(DatabaseImage.FullName).Build();
        await Task.WhenAll(_database.StartAsync(), _storage.StartAsync());
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    /// <summary>A fresh context outside the API, for arranging and asserting database state.</summary>
    public TourDockDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<TourDockDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AuditingInterceptor(TimeProvider.System))
            .Options);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Database", ConnectionString);
        builder.UseSetting("Jwt:SigningKey", "integration-tests-only-signing-key-0123456789abcdef");
        builder.UseSetting("RateLimiting:AuthPermitPerMinute", "10000");
        builder.UseSetting("RateLimiting:GlobalPermitPer10Seconds", "100000");
        builder.UseSetting("RateLimiting:WritesPerMinute", "10000");
        builder.UseSetting("RateLimiting:ReviewsPerMinute", "10000");
        builder.UseSetting("RateLimiting:AccountEmailsPerHour", "10000");
        builder.UseSetting("Jobs:SlotGeneration:Enabled", "false"); // tests trigger jobs explicitly
        builder.UseSetting("Jobs:BookingMaintenance:Enabled", "false");
        builder.UseSetting("Jobs:Outbox:Enabled", "false");
        builder.UseSetting("Jobs:Embeddings:Enabled", "false");
        builder.UseSetting("Jobs:AssistantRetention:Enabled", "false");
        builder.UseSetting("Assistant:ApiKey", "integration-tests-not-a-key");
        builder.UseSetting("Assistant:MaxTurnsPerConversation", "3");
        builder.UseSetting("RateLimiting:AssistantPerMinute", "10000");
        builder.UseSetting("RateLimiting:AssistantPerDay", "10000");
        builder.UseSetting("RateLimiting:SemanticSearchPerMinute", "10000");
        builder.UseSetting("RateLimiting:SemanticSearchPerDay", "10000");
        builder.UseSetting("RateLimiting:McpPerMinute", "10000");
        builder.UseSetting("RateLimiting:OAuthRegistrationsPerHour", "10000");
        builder.UseSetting("OAuth:Issuer", "http://localhost");
        builder.UseSetting("Payments:BaseUrl", "http://sandbox.test");
        builder.UseSetting("Payments:ApiKey", "integration-tests-api-key");
        builder.UseSetting("Payments:WebhookSecret", WebhookSecret);
        builder.UseSetting("Payments:AllowedReturnOrigins:0", WebOrigin);
        builder.UseSetting("Storage:ServiceUrl", StorageUrl);
        builder.UseSetting("Storage:PublicBaseUrl", $"{StorageUrl}/tour-photos");
        builder.UseSetting("Storage:AccessKey", "testaccesskey");
        builder.UseSetting("Storage:SecretKey", "testsecretkey123");
        builder.UseSetting("Email:Host", "smtp.test");
        builder.UseSetting("Email:FromAddress", "noreply@tourdock.test");
        builder.UseSetting("Email:WebBaseUrl", WebOrigin);
        // Most flows register and book straight away; EmailConfirmationTests switch the requirement on.
        builder.UseSetting("Accounts:RequireConfirmedEmail", "false");
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPaymentProvider>();
            services.AddSingleton<IPaymentProvider>(PaymentProvider);
            services.RemoveAll<IEmbeddingProvider>();
            services.AddSingleton<IEmbeddingProvider>(Embeddings);
            services.RemoveAll<ITravelAssistant>();
            services.AddSingleton<ITravelAssistant>(Assistant);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            services.AddHttpClient(TravelInfoHttpClients.ExchangeRates).ConfigurePrimaryHttpMessageHandler(() => ExchangeRatesHttp);
            services.AddHttpClient(TravelInfoHttpClients.Weather).ConfigurePrimaryHttpMessageHandler(() => WeatherHttp);
            services.AddHttpClient(PasswordScreenHttpClient.Name).ConfigurePrimaryHttpMessageHandler(() => PasswordsHttp);
            services.AddSingleton<IStartupFilter>(new PeerAddressFilter());
        });
        builder.UseSetting("Admin:Email", AdminEmail);
        builder.UseSetting("Admin:Password", AdminPassword);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }

        await _storage.DisposeAsync();
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "deploy", "db", "Dockerfile")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("deploy/db/Dockerfile not found above the test output folder.");
    }

    /// <summary>Test-only: the in-memory server has no network peer, so a header picks the client's address.</summary>
    public const string PeerHeader = "X-Test-Peer-Ip";

    private sealed class PeerAddressFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(PeerHeader, out var peer))
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(peer!);
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
