using ShiftClub.Shared.Contracts.Employees;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Application.Abstractions;

public interface IEmployeeAdminService
{
    Task<IReadOnlyList<EmployeeListItemDto>> ListEmployeesAsync(CancellationToken cancellationToken = default);
    Task<EmployeeListItemDto> CreateEmployeeAsync(CreateEmployeeRequest request, Guid actorId, CancellationToken cancellationToken = default);
    Task<EmployeeListItemDto> UpdateEmployeeAsync(Guid id, UpdateEmployeeRequest request, Guid actorId, CancellationToken cancellationToken = default);
    Task SetPasswordAsync(Guid id, SetEmployeePasswordRequest request, Guid actorId, CancellationToken cancellationToken = default);
    Task DeleteEmployeeAsync(Guid id, Guid actorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PermissionDto>> ListPermissionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkShiftDto>> GetShiftsAsync(DateOnly from, DateOnly to, Guid? employeeId, CancellationToken cancellationToken = default);
    Task<WorkShiftDto> CreateShiftAsync(CreateWorkShiftRequest request, Guid actorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkShiftDto>> BulkCreateShiftsAsync(BulkCreateWorkShiftsRequest request, Guid actorId, CancellationToken cancellationToken = default);
    Task<WorkShiftDto> UpdateShiftAsync(Guid shiftId, UpdateWorkShiftRequest request, Guid actorId, CancellationToken cancellationToken = default);
    Task<WorkShiftDto> CancelShiftAsync(Guid shiftId, Guid actorId, CancellationToken cancellationToken = default);
    Task<WorkShiftDto> ClockInAsync(Guid shiftId, ClockActionRequest request, Guid actorId, CancellationToken cancellationToken = default);
    Task<WorkShiftDto> ClockOutAsync(Guid shiftId, ClockActionRequest request, Guid actorId, CancellationToken cancellationToken = default);
    Task<WorkShiftDto> StartBreakAsync(Guid shiftId, Guid actorId, CancellationToken cancellationToken = default);
    Task<WorkShiftDto> EndBreakAsync(Guid shiftId, Guid actorId, CancellationToken cancellationToken = default);
    Task<WorkShiftDto> MarkAbsentAsync(Guid shiftId, ClockActionRequest request, Guid actorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayrollAccrualDto>> GetAccrualsAsync(DateOnly from, DateOnly to, Guid? employeeId, CancellationToken cancellationToken = default);
    Task<PayrollAccrualDto> CreateAccrualAsync(CreatePayrollAccrualRequest request, Guid actorId, CancellationToken cancellationToken = default);
    Task<PayrollAccrualDto> ApproveAccrualAsync(Guid id, Guid actorId, CancellationToken cancellationToken = default);
    Task<PayrollAccrualDto> MarkAccrualPaidAsync(Guid id, Guid actorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PayrollSummaryDto>> GetPayrollSummaryAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
