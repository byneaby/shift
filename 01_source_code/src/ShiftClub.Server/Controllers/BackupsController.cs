using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Backups;
using ShiftClub.Shared.ErrorCodes;
using ShiftClub.Shared.Permissions;
using ShiftClub.Server.Auth;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/backups")]
public sealed class BackupsController : ControllerBase
{
    private readonly IDatabaseBackupService _backups;

    public BackupsController(IDatabaseBackupService backups)
    {
        _backups = backups;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<BackupStatusDto>>> Get(CancellationToken cancellationToken)
    {
        var status = await _backups.GetStatusAsync(cancellationToken);
        return Ok(ApiResponse<BackupStatusDto>.Ok(status));
    }

    [HttpPost("run")]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<RunBackupResultDto>>> Run(CancellationToken cancellationToken)
    {
        var result = await _backups.RunAsync("manual", cancellationToken);
        return result.Success
            ? Ok(ApiResponse<RunBackupResultDto>.Ok(result))
            : BadRequest(ApiResponse<RunBackupResultDto>.Fail(CommonErrorCodes.InternalError, result.Message));
    }

    [HttpGet("download/{fileName}")]
    [Authorize(Roles = "owner")]
    public IActionResult Download(string fileName)
    {
        var path = _backups.ResolveFilePath(fileName);
        if (path is null)
            return NotFound(ApiResponse.Fail(CommonErrorCodes.NotFound, "Файл копии не найден"));

        return PhysicalFile(path, "application/octet-stream", Path.GetFileName(path));
    }
}
