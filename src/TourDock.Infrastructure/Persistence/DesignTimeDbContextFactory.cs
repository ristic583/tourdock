using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TourDock.Infrastructure.Persistence;

/// <summary>Used by `dotnet ef` (migrations, bundles) so it doesn't need to start the API.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TourDockDbContext>
{
    public TourDockDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? "Host=localhost;Database=tourdock;Username=tourdock;Password=design-time-only";

        var options = new DbContextOptionsBuilder<TourDockDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new TourDockDbContext(options);
    }
}
