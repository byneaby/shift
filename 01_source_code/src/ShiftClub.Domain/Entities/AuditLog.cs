namespace ShiftClub.Domain.Entities;

public class AuditLog : Common.Entity
{
    public Guid? BranchId { get; set; }
    public Guid? EmployeeId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? DetailsJson { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
