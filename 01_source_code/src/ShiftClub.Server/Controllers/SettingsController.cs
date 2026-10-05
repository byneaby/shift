using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Public;
using ShiftClub.Shared.Contracts.Settings;
using ShiftClub.Shared.ErrorCodes;
using ShiftClub.Shared.Permissions;
using ShiftClub.Infrastructure.Services;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly IClubSettingsService _settings;
    private readonly IWebHostEnvironment _env;

    public SettingsController(IClubSettingsService settings, IWebHostEnvironment env)
    {
        _settings = settings;
        _env = env;
    }

    [HttpGet("shell-admin")]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<ShellAdminPasswordStatusDto>>> GetShellAdmin(
        CancellationToken cancellationToken)
    {
        var status = await _settings.GetShellAdminStatusAsync(cancellationToken);
        return Ok(ApiResponse<ShellAdminPasswordStatusDto>.Ok(status));
    }

    [HttpPut("shell-admin-password")]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse>> SetShellAdminPassword(
        [FromBody] SetShellAdminPasswordRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _settings.SetShellAdminPasswordAsync(request.Password, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse.Ok());
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpGet("login-background")]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<LoginBackgroundDto>>> GetLoginBackground(
        CancellationToken cancellationToken)
    {
        var dto = await _settings.GetLoginBackgroundAsync(cancellationToken);
        return Ok(ApiResponse<LoginBackgroundDto>.Ok(dto));
    }

    [HttpPost("login-background")]
    [Authorize(Roles = "owner")]
    [RequestSizeLimit(12_000_000)]
    public async Task<ActionResult<ApiResponse<LoginBackgroundDto>>> UploadLoginBackground(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<LoginBackgroundDto>.Fail(CommonErrorCodes.ValidationFailed, "Файл пустой"));

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".gif"))
            return BadRequest(ApiResponse<LoginBackgroundDto>.Fail(CommonErrorCodes.ValidationFailed, "Нужен jpg/png/webp/gif"));

        try
        {
            var dir = Path.Combine(_env.ContentRootPath, "data", "branding");
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.EnumerateFiles(dir, "login-bg.*"))
            {
                try { System.IO.File.Delete(old); } catch { /* ignore */ }
            }

            var name = $"login-bg{ext}";
            var path = Path.Combine(dir, name);
            await using (var stream = System.IO.File.Create(path))
                await file.CopyToAsync(stream, cancellationToken);

            var url = $"/media/branding/{name}?v={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var dto = await _settings.SetLoginBackgroundUrlAsync(url, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<LoginBackgroundDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<LoginBackgroundDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpDelete("login-background")]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse>> ClearLoginBackground(CancellationToken cancellationToken)
    {
        await _settings.ClearLoginBackgroundAsync(GetEmployeeId(), cancellationToken);
        try
        {
            var dir = Path.Combine(_env.ContentRootPath, "data", "branding");
            if (Directory.Exists(dir))
            {
                foreach (var old in Directory.EnumerateFiles(dir, "login-bg.*"))
                    System.IO.File.Delete(old);
            }
        }
        catch { /* ignore */ }

        return Ok(ApiResponse.Ok());
    }

    [HttpGet("telegram")]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<TelegramBotSettingsDto>>> GetTelegram(CancellationToken cancellationToken)
    {
        var dto = await _settings.GetTelegramBotSettingsAsync(cancellationToken);
        return Ok(ApiResponse<TelegramBotSettingsDto>.Ok(dto));
    }

    [HttpPut("telegram")]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<TelegramBotSettingsDto>>> PutTelegram(
        [FromBody] UpdateTelegramBotSettingsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _settings.UpdateTelegramBotSettingsAsync(request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<TelegramBotSettingsDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<TelegramBotSettingsDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("telegram/broadcast")]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<TelegramBroadcastResultDto>>> BroadcastTelegram(
        [FromBody] TelegramBroadcastRequest request,
        [FromServices] TelegramBroadcastService broadcast,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await broadcast.BroadcastAsync(request, cancellationToken);
            return Ok(ApiResponse<TelegramBroadcastResultDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<TelegramBroadcastResultDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpGet("telegram/users")]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<TelegramUsersAdminDto>>> TelegramUsers(CancellationToken cancellationToken)
    {
        var dto = await _settings.GetTelegramUsersAdminAsync(cancellationToken);
        return Ok(ApiResponse<TelegramUsersAdminDto>.Ok(dto));
    }

    [HttpPost("telegram/users/customers/{id:guid}/unlink")]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<object>>> UnlinkCustomerTelegram(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            await _settings.UnlinkCustomerTelegramAsync(id, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { }));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.Fail(CommonErrorCodes.NotFound, "Клиент не найден"));
        }
    }

    [HttpPost("telegram/users/employees/{id:guid}/unlink")]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<object>>> UnlinkEmployeeTelegram(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            await _settings.UnlinkEmployeeTelegramAsync(id, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { }));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.Fail(CommonErrorCodes.NotFound, "Сотрудник не найден"));
        }
    }

    [HttpPost("telegram/users/make-staff")]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<object>>> MakeStaffFromTelegram(
        [FromBody] TelegramMakeStaffRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _settings.MakeStaffFromTelegramAsync(request, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { }));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpGet("engagement")]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<EngagementSettingsDto>>> GetEngagement(CancellationToken cancellationToken)
    {
        var dto = await _settings.GetEngagementSettingsAsync(cancellationToken);
        return Ok(ApiResponse<EngagementSettingsDto>.Ok(dto));
    }

    [HttpPut("engagement")]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<EngagementSettingsDto>>> PutEngagement(
        [FromBody] UpdateEngagementSettingsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _settings.UpdateEngagementSettingsAsync(request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<EngagementSettingsDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<EngagementSettingsDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpGet("marketing-promo")]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<MarketingPromoSettingsDto>>> GetMarketingPromo(CancellationToken cancellationToken)
    {
        var dto = await _settings.GetMarketingPromoSettingsAsync(cancellationToken);
        return Ok(ApiResponse<MarketingPromoSettingsDto>.Ok(dto));
    }

    [HttpPut("marketing-promo")]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<MarketingPromoSettingsDto>>> PutMarketingPromo(
        [FromBody] UpdateMarketingPromoRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _settings.UpdateMarketingPromoSettingsAsync(request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<MarketingPromoSettingsDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<MarketingPromoSettingsDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpGet("telegram/crm")]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<TelegramCrmSettingsDto>>> GetTelegramCrm(
        [FromServices] ITelegramCrmService crm,
        CancellationToken cancellationToken)
    {
        var stored = await _settings.GetTelegramCrmStoredAsync(cancellationToken);
        var dto = await _settings.GetTelegramCrmSettingsAsync(cancellationToken);
        var stats = await crm.GetStatsAsync(stored, cancellationToken);
        return Ok(ApiResponse<TelegramCrmSettingsDto>.Ok(dto with { Stats = stats }));
    }

    [HttpPut("telegram/crm")]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<TelegramCrmSettingsDto>>> PutTelegramCrm(
        [FromBody] UpdateTelegramCrmSettingsRequest request,
        [FromServices] ITelegramCrmService crm,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _settings.UpdateTelegramCrmSettingsAsync(request, GetEmployeeId(), cancellationToken);
            var stats = await crm.GetStatsAsync(
                await _settings.GetTelegramCrmStoredAsync(cancellationToken),
                cancellationToken);
            return Ok(ApiResponse<TelegramCrmSettingsDto>.Ok(dto with { Stats = stats }));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<TelegramCrmSettingsDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    private Guid? GetEmployeeId()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(id, out var gid) ? gid : null;
    }
}
