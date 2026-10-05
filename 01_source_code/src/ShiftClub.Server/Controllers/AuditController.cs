using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Audit;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/audit")]
public class AuditController : ControllerBase
{
    private readonly IAuditQueryService _audit;

    public AuditController(IAuditQueryService audit)
    {
        _audit = audit;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.AuditView, PermissionCodes.EmployeesManage)]
    public async Task<ActionResult<ApiResponse<AuditLogPageDto>>> Query(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] Guid? employeeId = null,
        [FromQuery] string? action = null,
        [FromQuery] string? q = null,
        [FromQuery] bool technical = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _audit.QueryAsync(from, to, employeeId, action, q, technical, page, pageSize, cancellationToken);
        return Ok(ApiResponse<AuditLogPageDto>.Ok(result));
    }

    [HttpGet("actions")]
    [RequirePermission(PermissionCodes.AuditView, PermissionCodes.EmployeesManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<string>>>> Actions(
        [FromQuery] bool technical = false,
        CancellationToken cancellationToken = default)
    {
        var list = await _audit.ListActionsAsync(technical, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<string>>.Ok(list));
    }
}
