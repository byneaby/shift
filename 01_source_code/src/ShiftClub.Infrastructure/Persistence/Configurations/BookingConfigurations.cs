using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftClub.Domain.Entities;

namespace ShiftClub.Infrastructure.Persistence.Configurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(40).IsRequired();
        builder.Property(x => x.ContactName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ContactPhone).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Comment).HasMaxLength(1000);
        builder.Property(x => x.CancelReason).HasMaxLength(500);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.PrepayMethod).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.PrepaidAmount).HasPrecision(18, 2);
        builder.Property(x => x.TotalEstimated).HasPrecision(18, 2);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100);
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.IdempotencyKey);
        builder.HasIndex(x => new { x.BranchId, x.StartsAt, x.EndsAt });
        builder.HasIndex(x => x.Status);
        builder.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Zone).WithMany().HasForeignKey(x => x.ZoneId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class BookingComputerConfiguration : IEntityTypeConfiguration<BookingComputer>
{
    public void Configure(EntityTypeBuilder<BookingComputer> builder)
    {
        builder.ToTable("booking_computers");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.BookingId, x.ComputerId }).IsUnique();
        builder.HasIndex(x => x.ComputerId);
        builder.HasOne(x => x.Booking).WithMany(x => x.Computers).HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Computer).WithMany().HasForeignKey(x => x.ComputerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
