using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Branches;
using ShiftClub.Shared.ErrorCodes;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/zones")]
public sealed class ZonesController : ControllerBase
{
    private readonly IZoneService _zones;

    public ZonesController(IZoneService zones) => _zones = zones;

    [HttpGet]
    [RequirePermission(
        PermissionCodes.ZonesView, PermissionCodes.ZonesManage,
        PermissionCodes.ComputersView, PermissionCodes.SessionsView,
        PermissionCodes.CustomersView, PermissionCodes.CustomersManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ZoneDto>>>> List(
        [FromQuery] Guid? branchId,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var list = await _zones.ListAsync(this.ResolveFilter(branchId), includeInactive, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ZoneDto>>.Ok(list));
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.ZonesManage)]
    public async Task<ActionResult<ApiResponse<ZoneDto>>> Create(
        [FromBody] UpsertZoneRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var zone = await _zones.CreateAsync(request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ZoneDto>.Ok(zone));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<ZoneDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.ZonesManage)]
    public async Task<ActionResult<ApiResponse<ZoneDto>>> Update(
        Guid id,
        [FromBody] UpsertZoneRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var zone = await _zones.UpdateAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ZoneDto>.Ok(zone));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ZoneDto>.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ZoneDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.ZonesManage)]
    public async Task<ActionResult<ApiResponse>> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _zones.DeactivateAsync(id, cancellationToken);
            return Ok(ApiResponse.Ok());
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
    }

    private Guid GetEmployeeId()
    {
        var raw = User.FindFirstValue("employee_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.Parse(raw!);
    }
}
