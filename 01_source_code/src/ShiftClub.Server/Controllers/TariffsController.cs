using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Sessions;
using ShiftClub.Shared.ErrorCodes;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class TariffsController : ControllerBase
{
    private readonly ISessionService _sessions;
    private readonly ShiftClubDbContext _db;

    public TariffsController(ISessionService sessions, ShiftClubDbContext db)
    {
        _sessions = sessions;
        _db = db;
    }

    [HttpGet]
    [RequirePermission(
        PermissionCodes.SessionsView, PermissionCodes.SessionsStart,
        PermissionCodes.TariffsManage, PermissionCodes.ComputersView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TariffDto>>>> GetAll(
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? zoneId,
        [FromQuery] bool includeInactive = false,
        [FromQuery] bool availableNow = false,
        CancellationToken cancellationToken = default)
    {
        var list = await _sessions.GetTariffsAsync(this.ResolveFilter(branchId), includeInactive, availableNow, zoneId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<TariffDto>>.Ok(list));
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<ApiResponse<TariffDto>>> Create(
        [FromBody] UpsertTariffRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var branchId = await this.ResolveBranchIdAsync(_db, request.BranchId, cancellationToken);
            var tariff = await _sessions.CreateTariffAsync(request with { BranchId = branchId }, cancellationToken);
            return Ok(ApiResponse<TariffDto>.Ok(tariff));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<TariffDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<ApiResponse<TariffDto>>> Update(
        Guid id,
        [FromBody] UpsertTariffRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var tariff = await _sessions.UpdateTariffAsync(id, request, cancellationToken);
            return Ok(ApiResponse<TariffDto>.Ok(tariff));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<TariffDto>.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<TariffDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<ApiResponse>> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _sessions.DeactivateTariffAsync(id, cancellationToken);
            return Ok(ApiResponse.Ok());
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
    }
}
