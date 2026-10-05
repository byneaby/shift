namespace ShiftClub.Domain.Entities;

public class AppSetting : Common.Entity
{
    public Guid? BranchId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
}
