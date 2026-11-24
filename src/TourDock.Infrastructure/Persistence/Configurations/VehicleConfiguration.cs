using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TourDock.Domain.Agencies;
using TourDock.Domain.Vehicles;

namespace TourDock.Infrastructure.Persistence.Configurations;

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("vehicles", t => t.HasCheckConstraint("ck_vehicles_capacity", $"capacity BETWEEN 1 AND {Vehicle.MaxCapacity}"));
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.Name).HasMaxLength(Vehicle.NameMaxLength);
        builder.HasOne<Agency>().WithMany().HasForeignKey(v => v.AgencyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(v => v.AgencyId);
        builder.HasQueryFilter(v => !v.IsDeleted);
    }
}
