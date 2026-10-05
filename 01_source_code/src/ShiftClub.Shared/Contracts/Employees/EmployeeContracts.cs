using ShiftClub.Shared.Enums;

namespace ShiftClub.Shared.Contracts.Employees;

public sealed record RoleDto(Guid Id, string Code, string Name, string? Description, IReadOnlyList<string> Permissions);

public sealed record PermissionDto(Guid Id, string Code, string Name, string GroupName);

public sealed record EmployeeListItemDto(
    Guid Id,
    string Login,
    string DisplayName,
    string? Email,
    string? Phone,
    Guid? BranchId,
    bool IsActive,
    EmployeePayType PayType,
    decimal HourlyRate,
    decimal MonthlySalary,
    decimal ShiftRate,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<string> Roles);

public sealed record CreateEmployeeRequest(
    string Login,
    string DisplayName,
    string Password,
    string? Email,
    string? Phone,
    EmployeePayType PayType,
    decimal HourlyRate,
    decimal MonthlySalary,
    decimal ShiftRate,
    IReadOnlyList<string> RoleCodes);

public sealed record UpdateEmployeeRequest(
    string DisplayName,
    string? Email,
    string? Phone,
    bool IsActive,
    EmployeePayType PayType,
    decimal HourlyRate,
    decimal MonthlySalary,
    decimal ShiftRate,
    IReadOnlyList<string> RoleCodes);

public sealed record SetEmployeePasswordRequest(string NewPassword);

public sealed record WorkShiftDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    DateOnly WorkDate,
    TimeOnly PlannedStart,
    TimeOnly PlannedEnd,
    WorkShiftStatus Status,
    DateTimeOffset? ActualStartAt,
    DateTimeOffset? ActualEndAt,
    int BreakMinutes,
    decimal WorkedHours,
    string? Comment);

public sealed record CreateWorkShiftRequest(
    Guid EmployeeId,
    DateOnly WorkDate,
    TimeOnly PlannedStart,
    TimeOnly PlannedEnd,
    string? Comment);

public sealed record UpdateWorkShiftRequest(
    Guid? EmployeeId,
    DateOnly WorkDate,
    TimeOnly PlannedStart,
    TimeOnly PlannedEnd,
    string? Comment);

public sealed record BulkCreateWorkShiftsRequest(
    Guid EmployeeId,
    DateOnly From,
    DateOnly To,
    TimeOnly PlannedStart,
    TimeOnly PlannedEnd,
    /// <summary>0=Sunday … 6=Saturday. Null = каждый день в диапазоне.</summary>
    IReadOnlyList<int>? Weekdays,
    string? Comment);

public sealed record ClockActionRequest(string? Comment);

public sealed record WorkShiftNoteDto(
    Guid Id,
    Guid WorkShiftId,
    Guid AuthorEmployeeId,
    string AuthorName,
    string Kind,
    string Text,
    DateTimeOffset CreatedAt);

public sealed record CreateWorkShiftNoteRequest(string Text, string? Kind);

public sealed record WorkBoardShiftDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    DateOnly WorkDate,
    TimeOnly PlannedStart,
    TimeOnly PlannedEnd,
    WorkShiftStatus Status,
    DateTimeOffset? ActualStartAt,
    DateTimeOffset? ActualEndAt,
    int BreakMinutes,
    decimal WorkedHours,
    string? Comment,
    bool IsMine,
    IReadOnlyList<WorkShiftNoteDto> Notes);

public sealed record WorkBoardDto(
    DateOnly Date,
    string TimeZoneId,
    WorkBoardShiftDto? MyShift,
    IReadOnlyList<WorkBoardShiftDto> Shifts);

public sealed record WorkPortalMeDto(
    Guid EmployeeId,
    string Login,
    string DisplayName,
    bool CanManageSchedule,
    IReadOnlyList<string> Permissions);

public sealed record PayrollAccrualDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    PayrollAccrualType Type,
    PayrollAccrualStatus Status,
    decimal Amount,
    string? Basis,
    string? Comment,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt);

public sealed record CreatePayrollAccrualRequest(
    Guid EmployeeId,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    PayrollAccrualType Type,
    decimal Amount,
    string? Basis,
    string? Comment);

public sealed record PayrollSummaryDto(
    Guid EmployeeId,
    string EmployeeName,
    decimal Accrued,
    decimal Paid,
    decimal Remaining,
    decimal WorkedHours,
    int ShiftsCompleted);
