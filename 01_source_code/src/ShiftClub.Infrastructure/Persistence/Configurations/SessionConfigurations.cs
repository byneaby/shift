using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftClub.Domain.Entities;

namespace ShiftClub.Infrastructure.Persistence.Configurations;

public class TariffConfiguration : IEntityTypeConfiguration<Tariff>
{
    public void Configure(EntityTypeBuilder<Tariff> builder)
    {
        builder.ToTable("tariffs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.BillingMode).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.DurationMode).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.PricePerHour).HasPrecision(18, 2);
        builder.Property(x => x.MinCharge).HasPrecision(18, 2);
        builder.Property(x => x.FixedPrice).HasPrecision(18, 2);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.ColorHex).HasMaxLength(16);
        builder.Property(x => x.AvailableFrom).HasColumnType("time");
        builder.Property(x => x.AvailableTo).HasColumnType("time");
        builder.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
        builder.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Zone).WithMany().HasForeignKey(x => x.ZoneId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class GamingSessionConfiguration : IEntityTypeConfiguration<GamingSession>
{
    public void Configure(EntityTypeBuilder<GamingSession> builder)
    {
        builder.ToTable("gaming_sessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.GuestName).HasMaxLength(200);
        builder.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.DurationMode).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.CancelReason).HasMaxLength(500);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100);
        builder.Property(x => x.BasePrice).HasPrecision(18, 2);
        builder.Property(x => x.DiscountAmount).HasPrecision(18, 2);
        builder.Property(x => x.PrepaidAppliedAmount).HasPrecision(18, 2);
        builder.Property(x => x.TotalPrice).HasPrecision(18, 2);
        builder.Property(x => x.PaidAmount).HasPrecision(18, 2);
        builder.Property(x => x.DebtAmount).HasPrecision(18, 2);
        builder.Property(x => x.RowVersion).IsConcurrencyToken();
        builder.HasIndex(x => x.ComputerId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.IdempotencyKey);
        builder.HasIndex(x => new { x.ComputerId, x.Status });
        // One live session per PC (Active/Paused). Status stored as string enum.
        builder.HasIndex(x => x.ComputerId)
            .IsUnique()
            .HasDatabaseName("ix_gaming_sessions_one_live_per_computer")
            .HasFilter("\"Status\" IN ('Active', 'Paused')");
        builder.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Computer).WithMany().HasForeignKey(x => x.ComputerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Zone).WithMany().HasForeignKey(x => x.ZoneId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Tariff).WithMany().HasForeignKey(x => x.TariffId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SessionHistoryEntryConfiguration : IEntityTypeConfiguration<SessionHistoryEntry>
{
    public void Configure(EntityTypeBuilder<SessionHistoryEntry> builder)
    {
        builder.ToTable("session_history");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.PriceBefore).HasPrecision(18, 2);
        builder.Property(x => x.PriceAfter).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.SessionId, x.CreatedAt });
        builder.HasOne(x => x.Session).WithMany(x => x.History).HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
