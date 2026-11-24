using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TourDock.Domain.Agencies;
using TourDock.Infrastructure.Identity;

namespace TourDock.Infrastructure.Persistence.Configurations;

internal sealed class AgencyConfiguration : IEntityTypeConfiguration<Agency>
{
    public void Configure(EntityTypeBuilder<Agency> builder)
    {
        builder.ToTable("agencies");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Name).HasMaxLength(Agency.NameMaxLength);
        builder.Property(a => a.ContactEmail).HasMaxLength(254);
        builder.Property(a => a.SuspensionReason).HasMaxLength(500);
        builder.HasIndex(a => a.Status);

        builder.OwnsMany(a => a.Members, member =>
        {
            member.ToTable("agency_members");
            member.WithOwner().HasForeignKey(m => m.AgencyId);
            member.HasKey(m => new { m.AgencyId, m.UserId });
            member.HasIndex(m => m.UserId);
            member.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
