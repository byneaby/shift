using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Setup;
using ShiftClub.Shared.ErrorCodes;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/setup")]
public sealed class SetupController : ControllerBase
{
    private readonly ISetupService _setup;

    public SetupController(ISetupService setup)
    {
        _setup = setup;
    }

    /// <summary>
    /// Чек-лист установки. Закрыт входом: в нём видно, сменили ли пароль из
    /// поставки, и такую подсказку снаружи отдавать нельзя.
    /// </summary>
    [HttpGet]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<SetupStatusDto>>> Status(CancellationToken cancellationToken) =>
        Ok(ApiResponse<SetupStatusDto>.Ok(await _setup.GetStatusAsync(cancellationToken)));

    /// <summary>Применяет ответы мастера. Только владелец: тут меняется его пароль и ключ лицензии.</summary>
    [HttpPost]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<ApplySetupResultDto>>> Apply(
        [FromBody] ApplySetupRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _setup.ApplyAsync(request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ApplySetupResultDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ApplySetupResultDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    private Guid GetEmployeeId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
