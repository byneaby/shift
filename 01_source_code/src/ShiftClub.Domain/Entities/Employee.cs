using ShiftClub.Shared.Enums;

namespace ShiftClub.Domain.Entities;

public class Employee : Common.Entity
{
    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public string Login { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    /// <summary>ИИН РК — ровно 12 цифр.</summary>
    public string? Iin { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Привязанный Telegram user id (через Mini App / бот).</summary>
    public long? TelegramUserId { get; set; }
    public DateTimeOffset? TelegramLinkedAt { get; set; }

    public EmployeePayType PayType { get; set; } = EmployeePayType.Hourly;
    public decimal HourlyRate { get; set; }
    public decimal MonthlySalary { get; set; }
    public decimal ShiftRate { get; set; }

    public EmployeeCredential? Credential { get; set; }
    public ICollection<EmployeeRole> EmployeeRoles { get; set; } = new List<EmployeeRole>();
}
