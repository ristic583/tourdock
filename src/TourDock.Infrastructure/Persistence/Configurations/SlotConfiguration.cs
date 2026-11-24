using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TourDock.Domain.Agencies;
using TourDock.Domain.Slots;
using TourDock.Domain.Tours;
using TourDock.Domain.Vehicles;

namespace TourDock.Infrastructure.Persistence.Configurations;

internal sealed class SlotConfiguration : IEntityTypeConfiguration<Slot>
{
    public void Configure(EntityTypeBuilder<Slot> builder)
    {
        // "One vehicle, one departure at a time" is enforced by an exclusion constraint added in the migration.
        builder.ToTable("slots", t =>
        {
            t.HasCheckConstraint("ck_slots_capacity", "capacity >= 1");
            t.HasCheckConstraint("ck_slots_reserved_seats", "reserved_seats BETWEEN 0 AND capacity");
            t.HasCheckConstraint("ck_slots_time_range", "ends_at > starts_at");
        });
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Version).IsRowVersion();

        builder.HasOne<Tour>().WithMany().HasForeignKey(s => s.TourId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Agency>().WithMany().HasForeignKey(s => s.AgencyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Vehicle>().WithMany().HasForeignKey(s => s.VehicleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ScheduleRule>().WithMany().HasForeignKey(s => s.ScheduleRuleId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(s => new { s.TourId, s.StartsAt });
        builder.HasIndex(s => new { s.AgencyId, s.StartsAt });
        builder.HasIndex(s => s.StartsAt).HasFilter("status = 'Open'");
        builder.HasIndex(s => new { s.LocalDate, s.TourId }).HasFilter("status = 'Open'"); // search by date

        // Makes slot generation idempotent: a rule can't produce the same departure twice.
        builder.HasIndex(s => new { s.ScheduleRuleId, s.StartsAt }).IsUnique().HasFilter("schedule_rule_id IS NOT NULL");
    }
}
