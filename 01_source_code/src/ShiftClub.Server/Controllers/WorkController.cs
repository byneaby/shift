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

/// <summary>Employee self-service work board at /work — schedule, clock, notes.</summary>
[ApiController]
[Authorize]
[Route("api/work")]
public class WorkController : ControllerBase
{
    private readonly IWorkPortalService _work;
    private readonly IEmployeeAdminService _employees;

    public WorkController(IWorkPortalService work, IEmployeeAdminService employees)
    {
        _work = work;
        _employees = employees;
    }

    [HttpGet("me")]
    public async Task<ActionResult<ApiResponse<WorkPortalMeDto>>> Me(CancellationToken cancellationToken)
    {
        try
        {
            var me = await _work.GetMeAsync(GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<WorkPortalMeDto>.Ok(me));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<WorkPortalMeDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
    }

    [HttpGet("board")]
    public async Task<ActionResult<ApiResponse<WorkBoardDto>>> Board(
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
        var board = await _work.GetBoardAsync(date, GetEmployeeId(), cancellationToken);
        return Ok(ApiResponse<WorkBoardDto>.Ok(board));
    }

    [HttpGet("my-shifts")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WorkBoardShiftDto>>>> MyShifts(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var me = await _work.GetMeAsync(GetEmployeeId(), cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5));
        var f = from ?? today.AddDays(-7);
        var t = to ?? today.AddDays(14);
        var list = await _work.GetMyShiftsAsync(f, t, me.EmployeeId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<WorkBoardShiftDto>>.Ok(list));
    }

    [HttpPost("shifts/{id:guid}/clock-in")]
    public Task<ActionResult<ApiResponse<WorkShiftDto>>> ClockIn(Guid id, [FromBody] ClockActionRequest? request, CancellationToken cancellationToken)
        => Act(id, canManage => _work.ClockInAsync(id, request ?? new ClockActionRequest(null), GetEmployeeId(), canManage, cancellationToken));

    [HttpPost("shifts/{id:guid}/clock-out")]
    public Task<ActionResult<ApiResponse<WorkShiftDto>>> ClockOut(Guid id, [FromBody] ClockActionRequest? request, CancellationToken cancellationToken)
        => Act(id, canManage => _work.ClockOutAsync(id, request ?? new ClockActionRequest(null), GetEmployeeId(), canManage, cancellationToken));

    [HttpPost("shifts/{id:guid}/break-start")]
    public Task<ActionResult<ApiResponse<WorkShiftDto>>> BreakStart(Guid id, CancellationToken cancellationToken)
        => Act(id, canManage => _work.StartBreakAsync(id, GetEmployeeId(), canManage, cancellationToken));

    [HttpPost("shifts/{id:guid}/break-end")]
    public Task<ActionResult<ApiResponse<WorkShiftDto>>> BreakEnd(Guid id, CancellationToken cancellationToken)
        => Act(id, canManage => _work.EndBreakAsync(id, GetEmployeeId(), canManage, cancellationToken));

    [HttpGet("shifts/{id:guid}/notes")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WorkShiftNoteDto>>>> Notes(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var me = await _work.GetMeAsync(GetEmployeeId(), cancellationToken);
            var list = await _work.GetNotesAsync(id, me.EmployeeId, me.CanManageSchedule, cancellationToken);
            return Ok(ApiResponse<IReadOnlyList<WorkShiftNoteDto>>.Ok(list));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<IReadOnlyList<WorkShiftNoteDto>>.Fail(CommonErrorCodes.Forbidden, ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<IReadOnlyList<WorkShiftNoteDto>>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
    }

    [HttpPost("shifts/{id:guid}/notes")]
    public async Task<ActionResult<ApiResponse<WorkShiftNoteDto>>> AddNote(
        Guid id,
        [FromBody] CreateWorkShiftNoteRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var me = await _work.GetMeAsync(GetEmployeeId(), cancellationToken);
            var note = await _work.AddNoteAsync(id, request, me.EmployeeId, me.CanManageSchedule, cancellationToken);
            return Ok(ApiResponse<WorkShiftNoteDto>.Ok(note));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<WorkShiftNoteDto>.Fail(CommonErrorCodes.Forbidden, ex.Message));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<WorkShiftNoteDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    // —— Admin schedule (same portal, schedules.manage) ——

    [HttpGet("employees")]
    [RequirePermission(PermissionCodes.SchedulesManage, PermissionCodes.EmployeesManage, PermissionCodes.EmployeesView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeeListItemDto>>>> Employees(CancellationToken cancellationToken)
    {
        var list = await _employees.ListEmployeesAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<EmployeeListItemDto>>.Ok(list.Where(e => e.IsActive).ToList()));
    }

    [HttpGet("shifts")]
    [RequirePermission(PermissionCodes.SchedulesManage, PermissionCodes.EmployeesManage, PermissionCodes.EmployeesView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WorkShiftDto>>>> Shifts(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? employeeId,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5));
        var list = await _employees.GetShiftsAsync(from ?? today.AddDays(-7), to ?? today.AddDays(14), employeeId, cancellationToken);
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
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WorkShiftDto>>>> BulkCreate(
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
    public async Task<ActionResult<ApiResponse<WorkShiftDto>>> Cancel(Guid id, CancellationToken cancellationToken)
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

    private async Task<ActionResult<ApiResponse<WorkShiftDto>>> Act(Guid id, Func<bool, Task<WorkShiftDto>> action)
    {
        try
        {
            var me = await _work.GetMeAsync(GetEmployeeId(), CancellationToken.None);
            var item = await action(me.CanManageSchedule);
            return Ok(ApiResponse<WorkShiftDto>.Ok(item));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<WorkShiftDto>.Fail(CommonErrorCodes.Forbidden, ex.Message));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<WorkShiftDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    private Guid GetEmployeeId()
    {
        var raw = User.FindFirstValue("employee_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.Parse(raw!);
    }
}
