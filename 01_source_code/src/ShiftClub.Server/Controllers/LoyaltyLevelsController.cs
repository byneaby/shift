using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Customers;
using ShiftClub.Shared.ErrorCodes;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/loyalty-levels")]
public sealed class LoyaltyLevelsController : ControllerBase
{
    private readonly ILoyaltyService _loyalty;

    public LoyaltyLevelsController(ILoyaltyService loyalty) => _loyalty = loyalty;

    [HttpGet]
    [RequirePermission(PermissionCodes.CustomersView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LoyaltyLevelDto>>>> List(
        [FromQuery] Guid? branchId,
        CancellationToken cancellationToken)
    {
        var list = await _loyalty.ListAsync(branchId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<LoyaltyLevelDto>>.Ok(list));
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<LoyaltyLevelDto>>> Create(
        [FromBody] UpsertLoyaltyLevelRequest request,
        [FromQuery] Guid? branchId,
        CancellationToken cancellationToken)
    {
        try
        {
            var employeeId = Guid.Parse(User.FindFirst("employee_id")!.Value);
            var row = await _loyalty.CreateAsync(branchId, request, employeeId, cancellationToken);
            return Ok(ApiResponse<LoyaltyLevelDto>.Ok(row));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<LoyaltyLevelDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<LoyaltyLevelDto>.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<LoyaltyLevelDto>>> Update(
        Guid id,
        [FromBody] UpsertLoyaltyLevelRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var employeeId = Guid.Parse(User.FindFirst("employee_id")!.Value);
            var row = await _loyalty.UpdateAsync(id, request, employeeId, cancellationToken);
            return Ok(ApiResponse<LoyaltyLevelDto>.Ok(row));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<LoyaltyLevelDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<LoyaltyLevelDto>.Fail(CommonErrorCodes.NotFound, "Уровень не найден"));
        }
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            await _loyalty.DeleteAsync(id, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { }));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.Fail(CommonErrorCodes.NotFound, "Уровень не найден"));
        }
    }
}
