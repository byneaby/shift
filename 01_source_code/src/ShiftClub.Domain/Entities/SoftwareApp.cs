using ShiftClub.Domain.Common;

namespace ShiftClub.Domain.Entities;

public class SoftwareApp : Entity
{
    public Guid BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Games";
    public string ExePath { get; set; } = string.Empty;
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? IconPath { get; set; }
    public string? LaunchSoundUrl { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public int? MinAge { get; set; }
}
