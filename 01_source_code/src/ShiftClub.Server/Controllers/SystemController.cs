using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Diagnostics;
using ShiftClub.Shared.Contracts.ServerUpdates;
using ShiftClub.Shared.ErrorCodes;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/system")]
[RequirePermission(PermissionCodes.SettingsManage)]
public sealed class SystemController : ControllerBase
{
    private readonly IDiagnosticsService _diagnostics;
    private readonly IServerUpdateService _updates;

    public SystemController(IDiagnosticsService diagnostics, IServerUpdateService updates)
    {
        _diagnostics = diagnostics;
        _updates = updates;
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

    /// <summary>Что установлено и что вышло в канале обновлений. Ответ из кэша.</summary>
    [HttpGet("update")]
    public async Task<ActionResult<ApiResponse<ServerUpdateStatusDto>>> Update(CancellationToken cancellationToken) =>
        Ok(ApiResponse<ServerUpdateStatusDto>.Ok(await _updates.GetStatusAsync(cancellationToken)));

    /// <summary>Спрашивает канал заново, не считаясь с кэшем.</summary>
    [HttpPost("update/check")]
    public async Task<ActionResult<ApiResponse<ServerUpdateStatusDto>>> CheckUpdate(CancellationToken cancellationToken) =>
        Ok(ApiResponse<ServerUpdateStatusDto>.Ok(await _updates.CheckAsync(cancellationToken)));

    /// <summary>
    /// Запускает обновление. Только владельцу: сервер остановится, клуб на
    /// несколько минут останется без кассы.
    /// </summary>
    [HttpPost("update/start")]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<ServerUpdateStartResultDto>>> StartUpdate(
        [FromBody] StartServerUpdateRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _updates.StartAsync(GetEmployeeId(), request?.Version, cancellationToken);
        return result.Started
            ? Ok(ApiResponse<ServerUpdateStartResultDto>.Ok(result))
            : BadRequest(ApiResponse<ServerUpdateStartResultDto>.Fail(CommonErrorCodes.ValidationFailed, result.Message));
    }

    private Guid GetEmployeeId() =>
        Guid.TryParse(User.FindFirstValue("employee_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : Guid.Empty;
}
