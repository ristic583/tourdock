using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TourDock.Domain.Agencies;
using TourDock.Domain.Tours;
using TourDock.Domain.Vehicles;

namespace TourDock.Infrastructure.Persistence.Configurations;

internal sealed class TourConfiguration : IEntityTypeConfiguration<Tour>
{
    public void Configure(EntityTypeBuilder<Tour> builder)
    {
        builder.ToTable("tours", t =>
        {
            t.HasCheckConstraint("ck_tours_max_guests", $"max_guests BETWEEN 1 AND {Tour.MaxGuestsLimit}");
            t.HasCheckConstraint("ck_tours_duration", "duration > interval '0'");
        });
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Title).HasMaxLength(Tour.TitleMaxLength);
        builder.Property(t => t.Description).HasMaxLength(Tour.DescriptionMaxLength);

        builder.ComplexProperty(t => t.Location, location =>
        {
            location.Property(l => l.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsFixedLength();
            location.Property(l => l.City).HasColumnName("city").HasMaxLength(100);
            location.Property(l => l.Address).HasColumnName("address").HasMaxLength(250);
            location.Property(l => l.Latitude).HasColumnName("latitude");
            location.Property(l => l.Longitude).HasColumnName("longitude");
            location.Property(l => l.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(64);
        });

        builder.MapMoney(t => t.PricePerPerson, "price_per_person", required: false);
        builder.MapMoney(t => t.PrivatePrice, "private_price", required: false);
        builder.MapCancellationPolicy(t => t.CancellationPolicy);

        builder.HasOne<Agency>().WithMany().HasForeignKey(t => t.AgencyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => t.AgencyId);
        builder.HasIndex(t => t.Status);

        builder.HasMany(t => t.SeasonalPrices).WithOne().HasForeignKey(s => s.TourId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.ScheduleRules).WithOne().HasForeignKey(r => r.TourId).OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.Photos).WithOne().HasForeignKey(p => p.TourId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Photos).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(t => t.CoverPhoto);

        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}

internal sealed class TourPhotoConfiguration : IEntityTypeConfiguration<TourPhoto>
{
    public void Configure(EntityTypeBuilder<TourPhoto> builder)
    {
        builder.ToTable("tour_photos");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.StorageKey).HasMaxLength(200);
        builder.HasIndex(p => new { p.TourId, p.SortOrder });

        // Exactly one cover per tour (at most one row with is_cover = true).
        builder.HasIndex(p => p.TourId).IsUnique().HasFilter("is_cover").HasDatabaseName("ux_tour_photos_one_cover");
    }
}

internal sealed class SeasonalPriceConfiguration : IEntityTypeConfiguration<SeasonalPrice>
{
    public void Configure(EntityTypeBuilder<SeasonalPrice> builder)
    {
        // Non-overlap per tour is enforced by an exclusion constraint added in the migration.
        builder.ToTable("tour_seasonal_prices", t => t.HasCheckConstraint("ck_tour_seasonal_prices_dates", "valid_to >= valid_from"));
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.From).HasColumnName("valid_from");
        builder.Property(s => s.To).HasColumnName("valid_to");
        builder.MapMoney(s => s.PricePerPerson, "price_per_person", required: false);
        builder.MapMoney(s => s.PrivatePrice, "private_price", required: false);
    }
}

internal sealed class ScheduleRuleConfiguration : IEntityTypeConfiguration<ScheduleRule>
{
    public void Configure(EntityTypeBuilder<ScheduleRule> builder)
    {
        builder.ToTable("tour_schedule_rules", t =>
        {
            t.HasCheckConstraint("ck_tour_schedule_rules_days", "days > 0");
            t.HasCheckConstraint("ck_tour_schedule_rules_dates", "valid_until IS NULL OR valid_until >= valid_from");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.HasOne<Vehicle>().WithMany().HasForeignKey(r => r.VehicleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.VehicleId);
    }
}
