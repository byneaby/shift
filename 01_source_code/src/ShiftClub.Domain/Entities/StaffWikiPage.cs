using ShiftClub.Domain.Common;

namespace ShiftClub.Domain.Entities;

/// <summary>Статья внутренней инструкции для сотрудников (wiki в панели).</summary>
public class StaffWikiPage : Entity
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string BodyMarkdown { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public Guid? UpdatedByEmployeeId { get; set; }
}
