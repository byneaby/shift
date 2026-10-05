using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftClub.Domain.Entities;

namespace ShiftClub.Infrastructure.Persistence.Configurations;

public class LoyaltyLevelConfiguration : IEntityTypeConfiguration<LoyaltyLevel>
{
    public void Configure(EntityTypeBuilder<LoyaltyLevel> builder)
    {
        builder.ToTable("loyalty_levels");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.BonusPercent).HasPrecision(18, 2);
        builder.Property(x => x.TimeDiscountPercent).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
    }
}

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(200);
        builder.Property(x => x.Iin).HasMaxLength(12);
        builder.Property(x => x.Login).HasMaxLength(100);
        builder.Property(x => x.PasswordHash).HasMaxLength(500);
        builder.Property(x => x.PinHash).HasMaxLength(500);
        builder.Property(x => x.Balance).HasPrecision(18, 2);
        builder.Property(x => x.BonusBalance).HasPrecision(18, 2);
        builder.Property(x => x.TotalSpent).HasPrecision(18, 2);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.BlockReason).HasMaxLength(500);
        builder.HasIndex(x => new { x.BranchId, x.Phone }).IsUnique();
        builder.HasIndex(x => x.LastName);
        builder.HasIndex(x => x.LoggedInComputerId);
        builder.HasIndex(x => x.TelegramUserId);
        builder.HasIndex(x => new { x.BranchId, x.Iin }).IsUnique().HasFilter("\"Iin\" IS NOT NULL");
        builder.Property(x => x.ComfortLanguage).HasMaxLength(8);
        builder.HasOne(x => x.LoyaltyLevel).WithMany().HasForeignKey(x => x.LoyaltyLevelId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class TelegramAuthTicketConfiguration : IEntityTypeConfiguration<TelegramAuthTicket>
{
    public void Configure(EntityTypeBuilder<TelegramAuthTicket> builder)
    {
        builder.ToTable("telegram_auth_tickets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Purpose).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ResultMessage).HasMaxLength(500);
        builder.Property(x => x.PendingDisplayName).HasMaxLength(120);
        builder.Property(x => x.ClientNonce).HasMaxLength(64);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => new { x.Status, x.ExpiresAt });
        builder.HasIndex(x => x.ComputerId);
        builder.HasIndex(x => new { x.Purpose, x.ClientNonce, x.Status });
    }
}

public class CustomerBalanceTransactionConfiguration : IEntityTypeConfiguration<CustomerBalanceTransaction>
{
    public void Configure(EntityTypeBuilder<CustomerBalanceTransaction> builder)
    {
        builder.ToTable("customer_balance_transactions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.Direction).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.BalanceBefore).HasPrecision(18, 2);
        builder.Property(x => x.BalanceAfter).HasPrecision(18, 2);
        builder.Property(x => x.SourceType).HasMaxLength(100);
        builder.Property(x => x.SourceId).HasMaxLength(100);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100);
        builder.Property(x => x.Comment).HasMaxLength(500);
        builder.HasIndex(x => x.IdempotencyKey);
        builder.HasIndex(x => new { x.CustomerId, x.CreatedAt });
        builder.HasOne(x => x.Customer).WithMany(x => x.BalanceTransactions).HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CustomerTimeBankTransactionConfiguration : IEntityTypeConfiguration<CustomerTimeBankTransaction>
{
    public void Configure(EntityTypeBuilder<CustomerTimeBankTransaction> builder)
    {
        builder.ToTable("customer_time_bank_transactions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.Direction).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100);
        builder.Property(x => x.Comment).HasMaxLength(500);
        builder.HasIndex(x => x.IdempotencyKey);
        builder.HasIndex(x => new { x.CustomerId, x.CreatedAt });
        builder.HasIndex(x => new { x.CustomerId, x.ZoneId, x.CreatedAt });
        builder.HasOne(x => x.Customer).WithMany(x => x.TimeBankTransactions).HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Zone).WithMany().HasForeignKey(x => x.ZoneId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CustomerZoneTimeBankConfiguration : IEntityTypeConfiguration<CustomerZoneTimeBank>
{
    public void Configure(EntityTypeBuilder<CustomerZoneTimeBank> builder)
    {
        builder.ToTable("customer_zone_time_banks");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CustomerId, x.ZoneId }).IsUnique();
        builder.HasOne(x => x.Customer).WithMany(x => x.ZoneTimeBanks).HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Zone).WithMany().HasForeignKey(x => x.ZoneId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CustomerPackageConfiguration : IEntityTypeConfiguration<CustomerPackage>
{
    public void Configure(EntityTypeBuilder<CustomerPackage> builder)
    {
        builder.ToTable("customer_packages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PaidAmount).HasPrecision(18, 2);
        builder.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
