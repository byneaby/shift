using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftClub.Domain.Entities;

namespace ShiftClub.Infrastructure.Persistence.Configurations;

public class CaseDefinitionConfiguration : IEntityTypeConfiguration<CaseDefinition>
{
    public void Configure(EntityTypeBuilder<CaseDefinition> builder)
    {
        builder.ToTable("case_definitions");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.EconomicsNote).HasMaxLength(1000);
        builder.Property(x => x.ExpectedCostKzt).HasPrecision(18, 2);
        builder.Property(x => x.LimitsJson).HasColumnType("text");
    }
}

public class CasePrizeConfiguration : IEntityTypeConfiguration<CasePrize>
{
    public void Configure(EntityTypeBuilder<CasePrize> builder)
    {
        builder.ToTable("case_prizes");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CaseDefinitionId, x.PrizeCode }).IsUnique();
        builder.Property(x => x.PrizeCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.PayloadJson).HasColumnType("text").IsRequired();
        builder.Property(x => x.ImageUrl).HasMaxLength(500);
        builder.Property(x => x.CostEstimateKzt).HasPrecision(18, 2);
        builder.HasOne(x => x.CaseDefinition).WithMany(x => x.Prizes).HasForeignKey(x => x.CaseDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class CaseKeyLedgerConfiguration : IEntityTypeConfiguration<CaseKeyLedger>
{
    public void Configure(EntityTypeBuilder<CaseKeyLedger> builder)
    {
        builder.ToTable("case_key_ledger");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.IdempotencyKey).IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");
        builder.HasIndex(x => new { x.CustomerId, x.CreatedAt });
        builder.Property(x => x.IdempotencyKey).HasMaxLength(200);
        builder.Property(x => x.Comment).HasMaxLength(500);
        builder.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class CaseOpeningConfiguration : IEntityTypeConfiguration<CaseOpening>
{
    public void Configure(EntityTypeBuilder<CaseOpening> builder)
    {
        builder.ToTable("case_openings");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.IdempotencyKey).IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");
        builder.HasIndex(x => new { x.CaseDefinitionId, x.ClubDayKey, x.CasePrizeId });
        builder.HasIndex(x => new { x.CustomerId, x.CreatedAt });
        builder.Property(x => x.PrizeCodeSnapshot).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PrizeNameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PayloadJsonSnapshot).HasColumnType("text").IsRequired();
        builder.Property(x => x.ImageUrlSnapshot).HasMaxLength(500);
        builder.Property(x => x.ClubDayKey).HasMaxLength(16).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(200);
        builder.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.CaseDefinition).WithMany().HasForeignKey(x => x.CaseDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CasePrize).WithMany().HasForeignKey(x => x.CasePrizeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CaseUserRewardConfiguration : IEntityTypeConfiguration<CaseUserReward>
{
    public void Configure(EntityTypeBuilder<CaseUserReward> builder)
    {
        builder.ToTable("case_user_rewards");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.CaseOpeningId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
        builder.HasIndex(x => new { x.CustomerId, x.Status });
        builder.Property(x => x.PrizeCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("text").IsRequired();
        builder.Property(x => x.ImageUrl).HasMaxLength(500);
        builder.Property(x => x.ClaimNote).HasMaxLength(500);
        builder.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.CaseOpening).WithOne(x => x.Reward).HasForeignKey<CaseUserReward>(x => x.CaseOpeningId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
