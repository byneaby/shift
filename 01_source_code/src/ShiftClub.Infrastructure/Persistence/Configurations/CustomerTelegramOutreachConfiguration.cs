using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftClub.Domain.Entities;

namespace ShiftClub.Infrastructure.Persistence.Configurations;

public sealed class CustomerTelegramOutreachConfiguration : IEntityTypeConfiguration<CustomerTelegramOutreach>
{
    public void Configure(EntityTypeBuilder<CustomerTelegramOutreach> builder)
    {
        builder.ToTable("customer_telegram_outreach");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Preview).HasMaxLength(240);
        builder.HasIndex(x => new { x.CustomerId, x.SentAt });
        builder.HasIndex(x => new { x.Kind, x.SentAt });
        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
