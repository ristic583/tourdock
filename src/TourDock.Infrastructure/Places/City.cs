using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TourDock.Infrastructure.Places;

/// <summary>A seeded place travelers can search from (reference data, read only at runtime).</summary>
public sealed class City
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary><see cref="Application.Catalog.PlaceSearch.Normalize"/> of the name: lower case, no accents.</summary>
    public string SearchName { get; set; } = string.Empty;

    /// <summary>Another normalized name the place is known by, for example "belgrade" for Beograd.</summary>
    public string? SearchAlias { get; set; }

    public string CountryCode { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public int Population { get; set; }

    public string TimeZoneId { get; set; } = string.Empty;
}

internal sealed class CityConfiguration : IEntityTypeConfiguration<City>
{
    public void Configure(EntityTypeBuilder<City> builder)
    {
        builder.ToTable("cities");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(100);
        builder.Property(c => c.SearchName).HasMaxLength(100);
        builder.Property(c => c.SearchAlias).HasMaxLength(100);
        builder.Property(c => c.CountryCode).HasMaxLength(2).IsFixedLength();
        builder.Property(c => c.TimeZoneId).HasMaxLength(64);

        // text_pattern_ops lets "search_name LIKE 'sib%'" use the index whatever the database collation.
        builder.HasIndex(c => c.SearchName).HasOperators("text_pattern_ops");
        builder.HasIndex(c => c.SearchAlias).HasOperators("text_pattern_ops").HasFilter("search_alias IS NOT NULL");
    }
}

/// <summary>
/// Distance helpers backed by PostgreSQL's earthdistance extension. They exist only to be translated to SQL inside LINQ
/// queries (see the migration that creates td_within_radius and td_distance_m); calling them in .NET throws.
/// </summary>
public static class GeoFunctions
{
    /// <summary>
    /// Whether a point is within <paramref name="meters"/> of a centre. Uses earth_box first, so the GiST index on
    /// ll_to_earth(latitude, longitude) narrows the rows before the exact distance check.
    /// </summary>
    public static bool WithinRadius(double latitude, double longitude, double centerLatitude, double centerLongitude, double meters) =>
        throw new InvalidOperationException("Only for use in database queries.");

    /// <summary>Great-circle distance in metres.</summary>
    public static double DistanceMeters(double latitude, double longitude, double centerLatitude, double centerLongitude) =>
        throw new InvalidOperationException("Only for use in database queries.");

    internal static void Map(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDbFunction(typeof(GeoFunctions).GetMethod(nameof(WithinRadius))!).HasName("td_within_radius");
        modelBuilder.HasDbFunction(typeof(GeoFunctions).GetMethod(nameof(DistanceMeters))!).HasName("td_distance_m");
    }
}
