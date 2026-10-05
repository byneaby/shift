using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.FloorMap;
using ShiftClub.Shared.ErrorCodes;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/floor-map")]
public sealed class FloorMapController : ControllerBase
{
    private readonly IFloorMapService _floorMap;

    public FloorMapController(IFloorMapService floorMap)
    {
        _floorMap = floorMap;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.ComputersView)]
    public async Task<ActionResult<ApiResponse<FloorMapDto>>> Get(
        [FromQuery] Guid? branchId,
        CancellationToken cancellationToken)
    {
        try
        {
            var map = await _floorMap.GetMapAsync(this.ResolveFilter(branchId), cancellationToken);
            return Ok(ApiResponse<FloorMapDto>.Ok(map));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<FloorMapDto>.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
    }

    [HttpPut("settings")]
    [RequirePermission(PermissionCodes.ComputersManage)]
    public async Task<ActionResult<ApiResponse<FloorMapDto>>> UpdateSettings(
        [FromBody] UpdateFloorMapSettingsRequest request,
        [FromQuery] Guid? branchId,
        CancellationToken cancellationToken)
    {
        try
        {
            var map = await _floorMap.UpdateSettingsAsync(this.ResolveFilter(branchId), request, cancellationToken);
            return Ok(ApiResponse<FloorMapDto>.Ok(map));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<FloorMapDto>.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<FloorMapDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("elements")]
    [RequirePermission(PermissionCodes.ComputersManage)]
    public async Task<ActionResult<ApiResponse<FloorMapElementDto>>> CreateElement(
        [FromBody] UpsertFloorMapElementRequest request,
        [FromQuery] Guid? branchId,
        CancellationToken cancellationToken)
    {
        try
        {
            var el = await _floorMap.CreateElementAsync(this.ResolveFilter(branchId), request, cancellationToken);
            return Ok(ApiResponse<FloorMapElementDto>.Ok(el));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<FloorMapElementDto>.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<FloorMapElementDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPut("elements/{id:guid}")]
    [RequirePermission(PermissionCodes.ComputersManage)]
    public async Task<ActionResult<ApiResponse<FloorMapElementDto>>> UpdateElement(
        Guid id,
        [FromBody] UpsertFloorMapElementRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var el = await _floorMap.UpdateElementAsync(id, request, cancellationToken);
            return Ok(ApiResponse<FloorMapElementDto>.Ok(el));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<FloorMapElementDto>.Fail(CommonErrorCodes.NotFound, "Элемент не найден"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<FloorMapElementDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpDelete("elements/{id:guid}")]
    [RequirePermission(PermissionCodes.ComputersManage)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteElement(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            await _floorMap.DeleteElementAsync(id, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { }));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.Fail(CommonErrorCodes.NotFound, "Элемент не найден"));
        }
    }
}
