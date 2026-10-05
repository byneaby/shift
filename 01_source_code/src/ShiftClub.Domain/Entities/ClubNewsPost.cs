using ShiftClub.Domain.Common;

namespace ShiftClub.Domain.Entities;

/// <summary>Новость клуба: панель → витрина на Shell.</summary>
public class ClubNewsPost : Entity
{
    public Guid BranchId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    /// <summary>Info / Promo / Event / Maintenance</summary>
    public string Category { get; set; } = "Info";
    public bool IsPublished { get; set; } = true;
    public bool IsPinned { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset? PublishAt { get; set; }
    public DateTimeOffset? ExpireAt { get; set; }
}
