using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftClub.Domain.Entities;

namespace ShiftClub.Infrastructure.Persistence.Configurations;

public class StaffWikiPageConfiguration : IEntityTypeConfiguration<StaffWikiPage>
{
    public void Configure(EntityTypeBuilder<StaffWikiPage> builder)
    {
        builder.ToTable("staff_wiki_pages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Slug).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.BodyMarkdown).HasColumnType("text").IsRequired();
        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasIndex(x => x.SortOrder);
    }
}
