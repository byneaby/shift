using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Diagnostics;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/system")]
[RequirePermission(PermissionCodes.SettingsManage)]
public sealed class SystemController : ControllerBase
{
    private readonly IDiagnosticsService _diagnostics;

    public SystemController(IDiagnosticsService diagnostics)
    {
        _diagnostics = diagnostics;
    }

    /// <summary>Состояние сервера клуба: версия, база, копии, лицензия, ошибки.</summary>
    [HttpGet("status")]
    public async Task<ActionResult<ApiResponse<SystemStatusDto>>> Status(CancellationToken cancellationToken) =>
        Ok(ApiResponse<SystemStatusDto>.Ok(await _diagnostics.GetStatusAsync(cancellationToken)));

    [HttpGet("errors")]
    public ActionResult<ApiResponse<IReadOnlyList<ErrorGroupDto>>> Errors([FromQuery] int limit = 50) =>
        Ok(ApiResponse<IReadOnlyList<ErrorGroupDto>>.Ok(_diagnostics.GetErrors(Math.Clamp(limit, 1, 100))));

    /// <summary>Очищает список: нужно, чтобы после исправления видеть только новые ошибки.</summary>
    [HttpPost("errors/clear")]
    public ActionResult<ApiResponse<object>> ClearErrors()
    {
        _diagnostics.ClearErrors();
        return Ok(ApiResponse<object>.Ok(new { }));
    }
}
