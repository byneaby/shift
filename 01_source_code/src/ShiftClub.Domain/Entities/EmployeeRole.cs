namespace ShiftClub.Domain.Entities;

public class EmployeeRole
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
}
