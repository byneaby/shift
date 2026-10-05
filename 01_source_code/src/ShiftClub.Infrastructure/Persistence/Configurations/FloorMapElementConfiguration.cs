using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftClub.Domain.Entities;

namespace ShiftClub.Infrastructure.Persistence.Configurations;

public class FloorMapElementConfiguration : IEntityTypeConfiguration<FloorMapElement>
{
    public void Configure(EntityTypeBuilder<FloorMapElement> builder)
    {
        builder.ToTable("floor_map_elements");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Label).HasMaxLength(500);
        builder.Property(x => x.ColorHex).HasMaxLength(16);
        builder.Property(x => x.FillHex).HasMaxLength(16);
        builder.Property(x => x.ShowIcon).HasDefaultValue(true);
        builder.Property(x => x.RotationDeg).HasDefaultValue(0);
        builder.HasIndex(x => x.BranchId);
        builder.HasOne(x => x.Branch).WithMany(b => b.FloorMapElements).HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
