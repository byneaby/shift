using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftClub.Domain.Entities;

namespace ShiftClub.Infrastructure.Persistence.Configurations;

public class SoftwareAppConfiguration : IEntityTypeConfiguration<SoftwareApp>
{
    public void Configure(EntityTypeBuilder<SoftwareApp> builder)
    {
        builder.ToTable("software_apps");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ExePath).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Arguments).HasMaxLength(500);
        builder.Property(x => x.WorkingDirectory).HasMaxLength(1000);
        builder.Property(x => x.IconPath).HasMaxLength(1000);
        builder.Property(x => x.LaunchSoundUrl).HasMaxLength(1000);
        builder.HasIndex(x => new { x.BranchId, x.SortOrder });
    }
}
