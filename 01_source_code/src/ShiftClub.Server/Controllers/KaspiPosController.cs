using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.KaspiPos;
using ShiftClub.Shared.ErrorCodes;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/kaspi-pos")]
public sealed class KaspiPosController : ControllerBase
{
    private readonly IKaspiPosService _kaspi;

    public KaspiPosController(IKaspiPosService kaspi)
    {
        _kaspi = kaspi;
    }

    [HttpGet("status")]
    [RequirePermission(PermissionCodes.BarSell, PermissionCodes.BarView, PermissionCodes.CashView, PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<KaspiPosStatusDto>>> Status(CancellationToken cancellationToken)
    {
        var dto = await _kaspi.GetStatusAsync(cancellationToken);
        return Ok(ApiResponse<KaspiPosStatusDto>.Ok(dto));
    }

    [HttpGet("config")]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<KaspiPosConfigDto>>> Config(CancellationToken cancellationToken)
    {
        var dto = await _kaspi.GetConfigAsync(cancellationToken);
        return Ok(ApiResponse<KaspiPosConfigDto>.Ok(dto));
    }

    [HttpPut("config")]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<KaspiPosConfigDto>>> UpdateConfig(
        [FromBody] UpdateKaspiPosConfigRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _kaspi.UpdateHostAsync(request.Host, GetEmployeeId(), cancellationToken);
            var dto = await _kaspi.GetConfigAsync(cancellationToken);
            return Ok(ApiResponse<KaspiPosConfigDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<KaspiPosConfigDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("register")]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<KaspiPosRegisterResultDto>>> Register(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _kaspi.RegisterAsync(cancellationToken);
            if (!result.Success)
                return BadRequest(ApiResponse<KaspiPosRegisterResultDto>.Fail(CommonErrorCodes.ValidationFailed, result.Message ?? "Ошибка"));
            return Ok(ApiResponse<KaspiPosRegisterResultDto>.Ok(result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<KaspiPosRegisterResultDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    /// <summary>Запуск оплаты на Smart POS и ожидание результата (до ~3 мин).</summary>
    [HttpPost("pay")]
    [RequirePermission(PermissionCodes.BarSell, PermissionCodes.SessionsStart, PermissionCodes.SessionsExtend)]
    public async Task<ActionResult<ApiResponse<KaspiPosPaymentResultDto>>> Pay(
        [FromBody] KaspiPosPayRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _kaspi.PayAndWaitAsync(request.Amount, cancellationToken);
            if (!result.Success && result.Status == "disabled")
                return Ok(ApiResponse<KaspiPosPaymentResultDto>.Ok(result));
            if (!result.Success)
                return BadRequest(ApiResponse<KaspiPosPaymentResultDto>.Fail(CommonErrorCodes.ValidationFailed, result.Message ?? "Оплата не прошла"));
            return Ok(ApiResponse<KaspiPosPaymentResultDto>.Ok(result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<KaspiPosPaymentResultDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    private Guid GetEmployeeId()
    {
        var raw = User.FindFirstValue("employee_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.Parse(raw!);
    }
}
