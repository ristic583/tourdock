using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TourDock.Application.Abstractions.Data;
using TourDock.Domain.Agencies;
using TourDock.Domain.Bookings;
using TourDock.Domain.Payments;
using TourDock.Domain.Reviews;
using TourDock.Domain.Slots;
using TourDock.Domain.Tours;
using TourDock.Domain.Vehicles;
using TourDock.Infrastructure.Identity;

namespace TourDock.Infrastructure.Persistence;

public sealed class TourDockDbContext(DbContextOptions<TourDockDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IUnitOfWork
{
    public DbSet<Agency> Agencies => Set<Agency>();

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<Tour> Tours => Set<Tour>();

    public DbSet<Slot> Slots => Set<Slot>();

    public DbSet<Booking> Bookings => Set<Booking>();

    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<ReviewReport> ReviewReports => Set<ReviewReport>();

    public DbSet<Domain.Administration.AdminAction> AdminActions => Set<Domain.Administration.AdminAction>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<Refund> Refunds => Set<Refund>();

    public DbSet<Outbox.OutboxMessage> OutboxMessages => Set<Outbox.OutboxMessage>();

    public DbSet<Outbox.ProcessedEvent> ProcessedEvents => Set<Outbox.ProcessedEvent>();

    public DbSet<Outbox.SentNotification> SentNotifications => Set<Outbox.SentNotification>();

    public DbSet<Places.City> Cities => Set<Places.City>();

    public DbSet<Assistant.AssistantConversation> AssistantConversations => Set<Assistant.AssistantConversation>();

    public DbSet<Assistant.AssistantDailyUsage> AssistantDailyUsage => Set<Assistant.AssistantDailyUsage>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            throw new DuplicateKeyException(pg.ConstraintName, ex);
        }
    }

    public void Reset() => ChangeTracker.Clear();

    public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new EfTransaction(await Database.BeginTransactionAsync(cancellationToken));

    private sealed class EfTransaction(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction) : ITransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Needed for exclusion constraints that mix "=" on uuid with "&&" on ranges.
        modelBuilder.HasPostgresExtension("btree_gist");
        modelBuilder.HasPostgresExtension("pg_trgm"); // fast ILIKE search on city
        modelBuilder.HasPostgresExtension("cube"); // earthdistance builds on it
        modelBuilder.HasPostgresExtension("earthdistance"); // "tours within 25 km of Split"
        modelBuilder.HasPostgresExtension("vector"); // tour embeddings for semantic search
        Places.GeoFunctions.Map(modelBuilder);
        Embeddings.SemanticFunctions.Map(modelBuilder);
        base.OnModelCreating(modelBuilder); // Identity tables
        // Domain ids are created in code (Guid v7). ValueGeneratedNever tells EF that a new child added to a loaded
        // aggregate (e.g. a schedule rule on a tour) is an INSERT, not an UPDATE of an existing row.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TourDockDbContext).Assembly);
        OAuth.OAuthModel.Map(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored as readable strings, not magic numbers.
        configurationBuilder.Properties<AgencyStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<AgencyRole>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<VehicleType>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<TourStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<BookingMode>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<SlotStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<BookingType>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<BookingStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<CancelledBy>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<PaymentStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<RefundStatus>().HaveConversion<string>().HaveMaxLength(32);
    }
}
