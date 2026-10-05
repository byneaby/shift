using ShiftClub.Shared.Enums;

namespace ShiftClub.Domain.Entities;

public class WorkShift : Common.Entity
{
    public Guid BranchId { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public DateOnly WorkDate { get; set; }
    public TimeOnly PlannedStart { get; set; }
    public TimeOnly PlannedEnd { get; set; }
    public WorkShiftStatus Status { get; set; } = WorkShiftStatus.Scheduled;

    public DateTimeOffset? ActualStartAt { get; set; }
    public DateTimeOffset? ActualEndAt { get; set; }
    public DateTimeOffset? BreakStartedAt { get; set; }
    public int BreakMinutes { get; set; }
    public string? Comment { get; set; }

    public Guid? CreatedByEmployeeId { get; set; }
    public Guid? SubstituteEmployeeId { get; set; }

    public ICollection<WorkShiftNote> Notes { get; set; } = new List<WorkShiftNote>();
}

public class WorkShiftNote : Common.Entity
{
    public Guid WorkShiftId { get; set; }
    public WorkShift WorkShift { get; set; } = null!;

    public Guid AuthorEmployeeId { get; set; }
    public string AuthorName { get; set; } = "";
    /// <summary>General | Attendance | Admin</summary>
    public string Kind { get; set; } = "General";
    public string Text { get; set; } = "";
}

public class PayrollAccrual : Common.Entity
{
    public Guid BranchId { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public PayrollAccrualType Type { get; set; }
    public PayrollAccrualStatus Status { get; set; } = PayrollAccrualStatus.Draft;

    public decimal Amount { get; set; }
    public string? Basis { get; set; }
    public string? Comment { get; set; }

    public Guid CreatedByEmployeeId { get; set; }
    public Guid? ApprovedByEmployeeId { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
}
