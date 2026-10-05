namespace ShiftClub.Domain.Entities;

public class Role : Common.Entity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    public ICollection<EmployeeRole> EmployeeRoles { get; set; } = new List<EmployeeRole>();
}
