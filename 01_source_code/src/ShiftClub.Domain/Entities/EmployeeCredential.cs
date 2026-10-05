namespace ShiftClub.Domain.Entities;

public class EmployeeCredential : Common.Entity
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public string PasswordHash { get; set; } = string.Empty;
    public string? PinHash { get; set; }
    public int AccessFailedCount { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public DateTimeOffset? PasswordChangedAt { get; set; }
}
