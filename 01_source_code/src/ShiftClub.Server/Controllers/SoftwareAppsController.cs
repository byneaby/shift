using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.ClientLauncher;
using ShiftClub.Shared.ErrorCodes;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/software-apps")]
public sealed class SoftwareAppsController : ControllerBase
{
    private readonly ISoftwareAppService _apps;
    private readonly ShiftClubDbContext _db;
    private readonly IWebHostEnvironment _env;

    public SoftwareAppsController(ISoftwareAppService apps, ShiftClubDbContext db, IWebHostEnvironment env)
    {
        _apps = apps;
        _db = db;
        _env = env;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.ComputersView, PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SoftwareAppAdminDto>>>> List(
        CancellationToken cancellationToken)
    {
        var list = await _apps.ListAsync(null, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<SoftwareAppAdminDto>>.Ok(list));
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<SoftwareAppAdminDto>>> Create(
        [FromBody] UpsertSoftwareAppRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var branchId = await this.ResolveBranchIdAsync(_db, null, cancellationToken);
            var result = await _apps.CreateAsync(branchId, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<SoftwareAppAdminDto>.Ok(result));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<SoftwareAppAdminDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<SoftwareAppAdminDto>>> Update(
        Guid id,
        [FromBody] UpsertSoftwareAppRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _apps.UpdateAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<SoftwareAppAdminDto>.Ok(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<SoftwareAppAdminDto>.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<SoftwareAppAdminDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _apps.DeleteAsync(id, cancellationToken);
            return Ok(ApiResponse.Ok());
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
    }

    [HttpPost("{id:guid}/image")]
    [RequirePermission(PermissionCodes.SettingsManage)]
    [RequestSizeLimit(8_000_000)]
    public async Task<ActionResult<ApiResponse<SoftwareAppAdminDto>>> UploadImage(
        Guid id,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
            return BadRequest(ApiResponse<SoftwareAppAdminDto>.Fail(CommonErrorCodes.ValidationFailed, "Файл пустой"));

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".gif"))
            return BadRequest(ApiResponse<SoftwareAppAdminDto>.Fail(CommonErrorCodes.ValidationFailed, "Нужен jpg/png/webp"));

        try
        {
            // Must match Program.cs static files root (ContentRootPath), not GetCurrentDirectory().
            var dir = Path.Combine(_env.ContentRootPath, "data", "software-covers");
            Directory.CreateDirectory(dir);

            // Remove previous covers for this app (any extension) so stale files don't linger.
            foreach (var old in Directory.EnumerateFiles(dir, $"{id:N}.*"))
            {
                try { System.IO.File.Delete(old); } catch { /* ignore locked */ }
            }

            var name = $"{id:N}{ext}";
            var path = Path.Combine(dir, name);
            await using (var stream = System.IO.File.Create(path))
                await file.CopyToAsync(stream, cancellationToken);

            // Cache-bust so panel/Shell browsers reload the new file (same path otherwise).
            var url = $"/media/software/{name}?v={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var app = await _apps.SetIconPathAsync(id, url, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<SoftwareAppAdminDto>.Ok(app));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<SoftwareAppAdminDto>.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<SoftwareAppAdminDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("{id:guid}/sound")]
    [RequirePermission(PermissionCodes.SettingsManage)]
    [RequestSizeLimit(8_000_000)]
    public async Task<ActionResult<ApiResponse<SoftwareAppAdminDto>>> UploadSound(
        Guid id,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
            return BadRequest(ApiResponse<SoftwareAppAdminDto>.Fail(CommonErrorCodes.ValidationFailed, "Файл пустой"));

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".mp3" or ".wav" or ".ogg"))
            return BadRequest(ApiResponse<SoftwareAppAdminDto>.Fail(CommonErrorCodes.ValidationFailed, "Нужен mp3/wav/ogg"));

        try
        {
            var dir = Path.Combine(_env.ContentRootPath, "data", "software-sounds");
            Directory.CreateDirectory(dir);

            foreach (var old in Directory.EnumerateFiles(dir, $"{id:N}.*"))
            {
                try { System.IO.File.Delete(old); } catch { }
            }

            var name = $"{id:N}{ext}";
            var path = Path.Combine(dir, name);
            await using (var stream = System.IO.File.Create(path))
                await file.CopyToAsync(stream, cancellationToken);

            var url = $"/media/software-sounds/{name}?v={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var app = await _apps.SetLaunchSoundUrlAsync(id, url, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<SoftwareAppAdminDto>.Ok(app));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<SoftwareAppAdminDto>.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
    }

    [HttpDelete("{id:guid}/sound")]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<SoftwareAppAdminDto>>> ClearSound(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var dir = Path.Combine(_env.ContentRootPath, "data", "software-sounds");
            if (Directory.Exists(dir))
            {
                foreach (var old in Directory.EnumerateFiles(dir, $"{id:N}.*"))
                {
                    try { System.IO.File.Delete(old); } catch { }
                }
            }

            var app = await _apps.SetLaunchSoundUrlAsync(id, null, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<SoftwareAppAdminDto>.Ok(app));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<SoftwareAppAdminDto>.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
    }

    [HttpPost("import")]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<SoftwareAppsImportResult>>> Import(
        [FromBody] SoftwareAppsImportFile? file,
        [FromQuery] bool updateExisting = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var apps = file?.Apps;
            // Допускаем и «голый» массив в body через отдельный контракт ниже
            if (apps is null || apps.Count == 0)
                return BadRequest(ApiResponse<SoftwareAppsImportResult>.Fail(
                    CommonErrorCodes.ValidationFailed, "В JSON нет списка apps."));

            var branchId = await this.ResolveBranchIdAsync(_db, null, cancellationToken);
            var result = await _apps.ImportAsync(branchId, apps, GetEmployeeId(), updateExisting, cancellationToken);
            return Ok(ApiResponse<SoftwareAppsImportResult>.Ok(result));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<SoftwareAppsImportResult>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("import-raw")]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<SoftwareAppsImportResult>>> ImportRaw(
        [FromBody] List<UpsertSoftwareAppRequest>? apps,
        [FromQuery] bool updateExisting = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (apps is null || apps.Count == 0)
                return BadRequest(ApiResponse<SoftwareAppsImportResult>.Fail(
                    CommonErrorCodes.ValidationFailed, "Пустой список игр."));

            var branchId = await this.ResolveBranchIdAsync(_db, null, cancellationToken);
            var result = await _apps.ImportAsync(branchId, apps, GetEmployeeId(), updateExisting, cancellationToken);
            return Ok(ApiResponse<SoftwareAppsImportResult>.Ok(result));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<SoftwareAppsImportResult>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    private Guid GetEmployeeId()
    {
        var raw = User.FindFirstValue("employee_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.Parse(raw!);
    }
}
