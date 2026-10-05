using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftClub.Domain.Entities;

namespace ShiftClub.Infrastructure.Persistence.Configurations;

public class ClubNewsPostConfiguration : IEntityTypeConfiguration<ClubNewsPost>
{
    public void Configure(EntityTypeBuilder<ClubNewsPost> builder)
    {
        builder.ToTable("club_news_posts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.ImageUrl).HasMaxLength(1000);
        builder.Property(x => x.Category).HasMaxLength(40).IsRequired();
        builder.HasIndex(x => new { x.BranchId, x.IsPublished, x.IsPinned, x.SortOrder });
    }
}
