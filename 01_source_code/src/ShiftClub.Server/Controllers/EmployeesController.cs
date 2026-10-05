using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Employees;
using ShiftClub.Shared.ErrorCodes;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/employees")]
public class EmployeesController : ControllerBase
{
    private readonly IEmployeeAdminService _employees;

    public EmployeesController(IEmployeeAdminService employees)
    {
        _employees = employees;
    }

    [HttpGet]
    [RequirePermission(
        PermissionCodes.EmployeesView, PermissionCodes.EmployeesManage,
        PermissionCodes.PayrollView, PermissionCodes.SchedulesManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeeListItemDto>>>> List(CancellationToken cancellationToken)
    {
        var list = await _employees.ListEmployeesAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<EmployeeListItemDto>>.Ok(list));
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.EmployeesManage)]
    public async Task<ActionResult<ApiResponse<EmployeeListItemDto>>> Create(
        [FromBody] CreateEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var item = await _employees.CreateEmployeeAsync(request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<EmployeeListItemDto>.Ok(item));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<EmployeeListItemDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.EmployeesManage)]
    public async Task<ActionResult<ApiResponse<EmployeeListItemDto>>> Update(
        Guid id,
        [FromBody] UpdateEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var item = await _employees.UpdateEmployeeAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<EmployeeListItemDto>.Ok(item));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<EmployeeListItemDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<EmployeeListItemDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("{id:guid}/password")]
    [RequirePermission(PermissionCodes.EmployeesManage)]
    public async Task<ActionResult<ApiResponse<object>>> SetPassword(
        Guid id,
        [FromBody] SetEmployeePasswordRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _employees.SetPasswordAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { ok = true }));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.EmployeesManage)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _employees.DeleteEmployeeAsync(id, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { ok = true }));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpGet("roles")]
    [RequirePermission(PermissionCodes.EmployeesView, PermissionCodes.EmployeesManage, PermissionCodes.RolesManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RoleDto>>>> Roles(CancellationToken cancellationToken)
    {
        var list = await _employees.ListRolesAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<RoleDto>>.Ok(list));
    }

    [HttpGet("permissions")]
    [RequirePermission(PermissionCodes.EmployeesView, PermissionCodes.EmployeesManage, PermissionCodes.RolesManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PermissionDto>>>> Permissions(CancellationToken cancellationToken)
    {
        var list = await _employees.ListPermissionsAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<PermissionDto>>.Ok(list));
    }

    [HttpGet("shifts")]
    [RequirePermission(PermissionCodes.SchedulesManage, PermissionCodes.EmployeesManage, PermissionCodes.EmployeesView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WorkShiftDto>>>> Shifts(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? employeeId,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var f = from ?? today.AddDays(-7);
        var t = to ?? today.AddDays(7);
        var list = await _employees.GetShiftsAsync(f, t, employeeId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<WorkShiftDto>>.Ok(list));
    }

    [HttpPost("shifts")]
    [RequirePermission(PermissionCodes.SchedulesManage, PermissionCodes.EmployeesManage)]
    public async Task<ActionResult<ApiResponse<WorkShiftDto>>> CreateShift(
        [FromBody] CreateWorkShiftRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var item = await _employees.CreateShiftAsync(request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<WorkShiftDto>.Ok(item));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<WorkShiftDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("shifts/bulk")]
    [RequirePermission(PermissionCodes.SchedulesManage, PermissionCodes.EmployeesManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WorkShiftDto>>>> BulkCreateShifts(
        [FromBody] BulkCreateWorkShiftsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var list = await _employees.BulkCreateShiftsAsync(request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<IReadOnlyList<WorkShiftDto>>.Ok(list));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<IReadOnlyList<WorkShiftDto>>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPut("shifts/{id:guid}")]
    [RequirePermission(PermissionCodes.SchedulesManage, PermissionCodes.EmployeesManage)]
    public async Task<ActionResult<ApiResponse<WorkShiftDto>>> UpdateShift(
        Guid id,
        [FromBody] UpdateWorkShiftRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var item = await _employees.UpdateShiftAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<WorkShiftDto>.Ok(item));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<WorkShiftDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("shifts/{id:guid}/cancel")]
    [RequirePermission(PermissionCodes.SchedulesManage, PermissionCodes.EmployeesManage)]
    public async Task<ActionResult<ApiResponse<WorkShiftDto>>> CancelShift(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var item = await _employees.CancelShiftAsync(id, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<WorkShiftDto>.Ok(item));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<WorkShiftDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("shifts/{id:guid}/clock-in")]
    [RequirePermission(PermissionCodes.SchedulesManage, PermissionCodes.EmployeesManage)]
    public async Task<ActionResult<ApiResponse<WorkShiftDto>>> ClockIn(
        Guid id,
        [FromBody] ClockActionRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var item = await _employees.ClockInAsync(id, request ?? new ClockActionRequest(null), GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<WorkShiftDto>.Ok(item));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<WorkShiftDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("shifts/{id:guid}/clock-out")]
    [RequirePermission(PermissionCodes.SchedulesManage, PermissionCodes.EmployeesManage)]
    public async Task<ActionResult<ApiResponse<WorkShiftDto>>> ClockOut(
        Guid id,
        [FromBody] ClockActionRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var item = await _employees.ClockOutAsync(id, request ?? new ClockActionRequest(null), GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<WorkShiftDto>.Ok(item));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<WorkShiftDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("shifts/{id:guid}/break-start")]
    [RequirePermission(PermissionCodes.SchedulesManage, PermissionCodes.EmployeesManage)]
    public async Task<ActionResult<ApiResponse<WorkShiftDto>>> BreakStart(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var item = await _employees.StartBreakAsync(id, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<WorkShiftDto>.Ok(item));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<WorkShiftDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("shifts/{id:guid}/break-end")]
    [RequirePermission(PermissionCodes.SchedulesManage, PermissionCodes.EmployeesManage)]
    public async Task<ActionResult<ApiResponse<WorkShiftDto>>> BreakEnd(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var item = await _employees.EndBreakAsync(id, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<WorkShiftDto>.Ok(item));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<WorkShiftDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("shifts/{id:guid}/absent")]
    [RequirePermission(PermissionCodes.SchedulesManage, PermissionCodes.EmployeesManage)]
    public async Task<ActionResult<ApiResponse<WorkShiftDto>>> Absent(
        Guid id,
        [FromBody] ClockActionRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var item = await _employees.MarkAbsentAsync(id, request ?? new ClockActionRequest(null), GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<WorkShiftDto>.Ok(item));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<WorkShiftDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpGet("payroll")]
    [RequirePermission(PermissionCodes.PayrollView, PermissionCodes.PayrollManage, PermissionCodes.EmployeesManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollAccrualDto>>>> Payroll(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? employeeId,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var f = from ?? new DateOnly(today.Year, today.Month, 1);
        var t = to ?? today;
        var list = await _employees.GetAccrualsAsync(f, t, employeeId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<PayrollAccrualDto>>.Ok(list));
    }

    [HttpGet("payroll/summary")]
    [RequirePermission(PermissionCodes.PayrollView, PermissionCodes.PayrollManage, PermissionCodes.EmployeesManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollSummaryDto>>>> PayrollSummary(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var f = from ?? new DateOnly(today.Year, today.Month, 1);
        var t = to ?? today;
        var list = await _employees.GetPayrollSummaryAsync(f, t, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<PayrollSummaryDto>>.Ok(list));
    }

    [HttpPost("payroll")]
    [RequirePermission(PermissionCodes.PayrollManage, PermissionCodes.EmployeesManage)]
    public async Task<ActionResult<ApiResponse<PayrollAccrualDto>>> CreatePayroll(
        [FromBody] CreatePayrollAccrualRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var item = await _employees.CreateAccrualAsync(request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<PayrollAccrualDto>.Ok(item));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<PayrollAccrualDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("payroll/{id:guid}/approve")]
    [RequirePermission(PermissionCodes.PayrollManage, PermissionCodes.EmployeesManage)]
    public async Task<ActionResult<ApiResponse<PayrollAccrualDto>>> ApprovePayroll(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var item = await _employees.ApproveAccrualAsync(id, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<PayrollAccrualDto>.Ok(item));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<PayrollAccrualDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("payroll/{id:guid}/paid")]
    [RequirePermission(PermissionCodes.PayrollManage, PermissionCodes.EmployeesManage)]
    public async Task<ActionResult<ApiResponse<PayrollAccrualDto>>> PayPayroll(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var item = await _employees.MarkAccrualPaidAsync(id, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<PayrollAccrualDto>.Ok(item));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<PayrollAccrualDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    private Guid GetEmployeeId()
    {
        var raw = User.FindFirstValue("employee_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.Parse(raw!);
    }
}
