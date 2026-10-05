using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftClub.Domain.Entities;

namespace ShiftClub.Infrastructure.Persistence.Configurations;

public class ComputerConfiguration : IEntityTypeConfiguration<Computer>
{
    public void Configure(EntityTypeBuilder<Computer> builder)
    {
        builder.ToTable("computers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DisplayName).HasMaxLength(200);
        builder.Property(x => x.WindowsName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.InstallationId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.Property(x => x.MacAddress).HasMaxLength(64);
        builder.Property(x => x.ClientVersion).HasMaxLength(50);
        builder.Property(x => x.WindowsVersion).HasMaxLength(100);
        builder.Property(x => x.CpuName).HasMaxLength(200);
        builder.Property(x => x.GpuName).HasMaxLength(200);
        builder.Property(x => x.ScreenResolution).HasMaxLength(50);
        builder.Property(x => x.RegistrationCode).HasMaxLength(16);
        builder.Property(x => x.DeviceTokenHash).HasMaxLength(500);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.Tags).HasMaxLength(500);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.StationKind).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.RowVersion).IsConcurrencyToken();
        builder.HasIndex(x => x.InstallationId).IsUnique();
        builder.HasIndex(x => x.MacAddress);
        builder.HasIndex(x => x.BranchId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.StationKind);
        builder.HasIndex(x => x.RegistrationCode);
        builder.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Zone).WithMany().HasForeignKey(x => x.ZoneId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.CurrentSession).WithOne()
            .HasForeignKey<Computer>(x => x.CurrentSessionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class ComputerHeartbeatConfiguration : IEntityTypeConfiguration<ComputerHeartbeat>
{
    public void Configure(EntityTypeBuilder<ComputerHeartbeat> builder)
    {
        builder.ToTable("computer_heartbeats");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.Property(x => x.ClientVersion).HasMaxLength(50);
        builder.Property(x => x.StatusNote).HasMaxLength(500);
        builder.HasIndex(x => new { x.ComputerId, x.ReceivedAt });
        builder.HasOne(x => x.Computer).WithMany(x => x.Heartbeats).HasForeignKey(x => x.ComputerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ComputerCommandConfiguration : IEntityTypeConfiguration<ComputerCommand>
{
    public void Configure(EntityTypeBuilder<ComputerCommand> builder)
    {
        builder.ToTable("computer_commands");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100);
        builder.Property(x => x.ErrorMessage).HasMaxLength(1000);
        builder.HasIndex(x => new { x.ComputerId, x.Status });
        builder.HasIndex(x => x.IdempotencyKey);
        builder.HasOne(x => x.Computer).WithMany(x => x.Commands).HasForeignKey(x => x.ComputerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
