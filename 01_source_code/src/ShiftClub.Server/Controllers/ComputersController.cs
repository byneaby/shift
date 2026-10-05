using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Computers;
using ShiftClub.Shared.ErrorCodes;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ComputersController : ControllerBase
{
    private readonly IComputerService _computers;

    public ComputersController(IComputerService computers)
    {
        _computers = computers;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.ComputersView, PermissionCodes.ComputersCommand, PermissionCodes.ComputersManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ComputerDto>>>> GetAll(
        [FromQuery] Guid? branchId,
        CancellationToken cancellationToken)
    {
        var list = await _computers.GetComputersAsync(branchId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ComputerDto>>.Ok(list));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.ComputersView, PermissionCodes.ComputersCommand, PermissionCodes.ComputersManage)]
    public async Task<ActionResult<ApiResponse<ComputerDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var computer = await _computers.GetByIdAsync(id, cancellationToken);
        if (computer is null)
            return NotFound(ApiResponse<ComputerDto>.Fail(CommonErrorCodes.NotFound, "ПК не найден"));
        return Ok(ApiResponse<ComputerDto>.Ok(computer));
    }

    [HttpPost("manual")]
    [RequirePermission(PermissionCodes.ComputersManage)]
    public async Task<ActionResult<ApiResponse<ComputerDto>>> CreateManual(
        [FromBody] CreateManualStationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _computers.CreateManualStationAsync(request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ComputerDto>.Ok(result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ComputerDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("{id:guid}/approve")]
    [RequirePermission(PermissionCodes.ComputersManage)]
    public async Task<ActionResult<ApiResponse<ApproveComputerResponse>>> Approve(
        Guid id,
        [FromBody] ApproveComputerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var employeeId = GetEmployeeId();
            var result = await _computers.ApproveAsync(id, request, employeeId, cancellationToken);
            return Ok(ApiResponse<ApproveComputerResponse>.Ok(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ApproveComputerResponse>.Fail(CommonErrorCodes.NotFound, "ПК не найден"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ApproveComputerResponse>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPut("{id:guid}/layout")]
    [RequirePermission(PermissionCodes.ComputersManage)]
    public async Task<ActionResult<ApiResponse<ComputerDto>>> UpdateLayout(
        Guid id,
        [FromBody] UpdateComputerLayoutRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _computers.UpdateLayoutAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ComputerDto>.Ok(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ComputerDto>.Fail(CommonErrorCodes.NotFound, "ПК не найден"));
        }
    }

    [HttpPost("{id:guid}/wake")]
    [RequirePermission(PermissionCodes.ComputersCommand, PermissionCodes.ComputersManage)]
    public async Task<ActionResult<ApiResponse<ComputerDto>>> Wake(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _computers.WakeAsync(id, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ComputerDto>.Ok(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ComputerDto>.Fail(CommonErrorCodes.NotFound, "ПК не найден"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ComputerDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("{id:guid}/commands")]
    [RequirePermission(PermissionCodes.ComputersCommand, PermissionCodes.ComputersManage)]
    public async Task<ActionResult<ApiResponse<ComputerCommandDto>>> SendCommand(
        Guid id,
        [FromBody] SendComputerCommandRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _computers.SendCommandAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ComputerCommandDto>.Ok(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ComputerCommandDto>.Fail(CommonErrorCodes.NotFound, "ПК не найден"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ComputerCommandDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpGet("{id:guid}/commands/{commandId:guid}")]
    [RequirePermission(PermissionCodes.ComputersView, PermissionCodes.ComputersCommand, PermissionCodes.ComputersManage)]
    public async Task<ActionResult<ApiResponse<ComputerCommandDto>>> GetCommand(
        Guid id,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        var result = await _computers.GetCommandAsync(id, commandId, cancellationToken);
        if (result is null)
            return NotFound(ApiResponse<ComputerCommandDto>.Fail(CommonErrorCodes.NotFound, "Команда не найдена"));
        return Ok(ApiResponse<ComputerCommandDto>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.ComputersManage)]
    public async Task<ActionResult<ApiResponse<ComputerDto>>> Update(
        Guid id,
        [FromBody] UpdateComputerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _computers.UpdateAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ComputerDto>.Ok(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ComputerDto>.Fail(CommonErrorCodes.NotFound, "ПК не найден"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ComputerDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("{id:guid}/revoke")]
    [RequirePermission(PermissionCodes.ComputersManage)]
    public async Task<ActionResult<ApiResponse<ComputerDto>>> Revoke(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _computers.RevokeAsync(id, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ComputerDto>.Ok(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ComputerDto>.Fail(CommonErrorCodes.NotFound, "ПК не найден"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ComputerDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.ComputersManage)]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _computers.DeleteAsync(id, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse.Ok("ПК удалён"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse.Fail(CommonErrorCodes.NotFound, "ПК не найден"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    private Guid GetEmployeeId()
    {
        var raw = User.FindFirstValue("employee_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.Parse(raw!);
    }
}
