using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShiftClub.Application.Abstractions;
using ShiftClub.Server.Security;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Auth;
using ShiftClub.Shared.ErrorCodes;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ITelegramAuthService _telegramAuth;

    public AuthController(IAuthService authService, ITelegramAuthService telegramAuth)
    {
        _authService = authService;
        _telegramAuth = telegramAuth;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(SecuritySetup.LoginPolicy)]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(ApiResponse<LoginResponse>.Fail(
                CommonErrorCodes.ValidationFailed,
                "Логин (или телефон) и пароль обязательны"));
        }

        try
        {
            var result = await _authService.LoginAsync(request, cancellationToken);
            if (result is null)
            {
                return Unauthorized(ApiResponse<LoginResponse>.Fail(
                    CommonErrorCodes.Unauthorized,
                    "Неверный логин или пароль"));
            }

            return Ok(ApiResponse<LoginResponse>.Ok(result));
        }
        catch (InvalidOperationException ex)
        {
            return Unauthorized(ApiResponse<LoginResponse>.Fail(
                CommonErrorCodes.Unauthorized,
                ex.Message));
        }
    }

    [HttpPost("telegram/start")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<StaffTelegramTicketDto>>> TelegramStart(
        [FromBody] StaffTelegramStartRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var nonce = (request?.ClientNonce ?? "").Trim();
            if (nonce.Length < 8)
            {
                return BadRequest(ApiResponse<StaffTelegramTicketDto>.Fail(
                    CommonErrorCodes.ValidationFailed,
                    "Обновите страницу входа и попробуйте снова."));
            }

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var dto = await _telegramAuth.CreateStaffLoginTicketAsync(nonce, ip, cancellationToken);
            return Ok(ApiResponse<StaffTelegramTicketDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<StaffTelegramTicketDto>.Fail(
                CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpGet("telegram/status/{ticketId:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<StaffTelegramTicketStatusDto>>> TelegramStatus(
        Guid ticketId,
        [FromQuery] string? clientNonce,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _telegramAuth.GetStaffLoginStatusAsync(ticketId, clientNonce, cancellationToken);
            return Ok(ApiResponse<StaffTelegramTicketStatusDto>.Ok(dto));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<StaffTelegramTicketStatusDto>.Fail(
                CommonErrorCodes.NotFound, "Код не найден"));
        }
    }

    [HttpPost("telegram/cancel/{ticketId:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object>>> TelegramCancel(
        Guid ticketId,
        [FromQuery] string? clientNonce,
        CancellationToken cancellationToken)
    {
        await _telegramAuth.CancelTicketAsync(ticketId, clientNonce, cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { }));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<object>>> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(ApiResponse<object>.Fail(
                CommonErrorCodes.ValidationFailed,
                "Текущий и новый пароль обязательны"));
        }

        try
        {
            var employeeId = Guid.Parse(
                User.FindFirstValue("employee_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _authService.ChangePasswordAsync(employeeId, request, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { ok = true }));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpGet("me")]
    [Authorize]
    public ActionResult<ApiResponse<object>> Me()
    {
        var payload = new
        {
            id = User.FindFirst("employee_id")?.Value,
            login = User.Identity?.Name,
            roles = User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToArray(),
            permissions = User.FindAll("permission").Select(c => c.Value).ToArray()
        };

        return Ok(ApiResponse<object>.Ok(payload));
    }
}
