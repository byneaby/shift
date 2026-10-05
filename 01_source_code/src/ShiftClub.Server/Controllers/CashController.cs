using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Cash;
using ShiftClub.Shared.ErrorCodes;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/cash")]
public class CashController : ControllerBase
{
    private readonly ICashService _cash;

    public CashController(ICashService cash)
    {
        _cash = cash;
    }

    [HttpGet("registers")]
    [RequirePermission(PermissionCodes.CashView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CashRegisterDto>>>> Registers(
        [FromQuery] Guid? branchId,
        CancellationToken cancellationToken)
    {
        var list = await _cash.GetRegistersAsync(branchId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<CashRegisterDto>>.Ok(list));
    }

    [HttpGet("shifts/current")]
    [RequirePermission(PermissionCodes.CashView, PermissionCodes.BarSell)]
    public async Task<ActionResult<ApiResponse<CashShiftDto>>> CurrentShift(CancellationToken cancellationToken)
    {
        var shift = await _cash.GetOpenShiftAsync(null, GetEmployeeId(), cancellationToken);
        if (shift is null)
            return NotFound(ApiResponse<CashShiftDto>.Fail(CommonErrorCodes.NotFound, "Нет открытой смены"));
        return Ok(ApiResponse<CashShiftDto>.Ok(shift));
    }

    [HttpPost("shifts/open")]
    [RequirePermission(PermissionCodes.CashShiftOpen)]
    public async Task<ActionResult<ApiResponse<CashShiftDto>>> Open(
        [FromBody] OpenCashShiftRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var shift = await _cash.OpenShiftAsync(request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<CashShiftDto>.Ok(shift));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<CashShiftDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CashShiftDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("shifts/{id:guid}/close")]
    [RequirePermission(PermissionCodes.CashShiftClose)]
    public async Task<ActionResult<ApiResponse<CashShiftDto>>> Close(
        Guid id,
        [FromBody] CloseCashShiftRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var shift = await _cash.CloseShiftAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<CashShiftDto>.Ok(shift));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<CashShiftDto>.Fail(CommonErrorCodes.NotFound, "Смена не найдена"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CashShiftDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("shifts/{id:guid}/force-close")]
    [RequirePermission(PermissionCodes.CashShiftClose)]
    public async Task<ActionResult<ApiResponse<CashShiftDto>>> ForceClose(
        Guid id,
        [FromBody] CloseCashShiftRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var shift = await _cash.ForceCloseShiftAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<CashShiftDto>.Ok(shift));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<CashShiftDto>.Fail(CommonErrorCodes.NotFound, "Смена не найдена"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CashShiftDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpGet("shifts/{id:guid}/z-report")]
    [RequirePermission(PermissionCodes.CashView, PermissionCodes.ReportsView)]
    public async Task<ActionResult<ApiResponse<CashZReportDto>>> ZReport(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var report = await _cash.GetShiftReportAsync(id, cancellationToken);
            return Ok(ApiResponse<CashZReportDto>.Ok(report));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<CashZReportDto>.Fail(CommonErrorCodes.NotFound, "Смена не найдена"));
        }
    }

    [HttpPost("shifts/{id:guid}/movements")]
    [RequirePermission(PermissionCodes.CashMovement)]
    public async Task<ActionResult<ApiResponse<CashShiftDto>>> Movement(
        Guid id,
        [FromBody] CashMovementRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var shift = await _cash.AddMovementAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<CashShiftDto>.Ok(shift));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<CashShiftDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("sales")]
    [RequirePermission(PermissionCodes.CashSale)]
    public async Task<ActionResult<ApiResponse<ReceiptDto>>> Sale(
        [FromBody] CreateSaleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var receipt = await _cash.CreateSaleAsync(request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ReceiptDto>.Ok(receipt));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ReceiptDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpGet("shifts/{id:guid}/receipts")]
    [RequirePermission(PermissionCodes.CashView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ReceiptDto>>>> Receipts(
        Guid id,
        CancellationToken cancellationToken)
    {
        var list = await _cash.GetRecentReceiptsAsync(id, 50, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ReceiptDto>>.Ok(list));
    }

    [HttpPost("receipts/{id:guid}/refund")]
    [RequirePermission(PermissionCodes.CashSale, PermissionCodes.CashRefund)]
    public async Task<ActionResult<ApiResponse<ReceiptDto>>> Refund(
        Guid id,
        [FromBody] RefundReceiptRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var receipt = await _cash.RefundReceiptAsync(id, request?.Comment, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ReceiptDto>.Ok(receipt));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ReceiptDto>.Fail(CommonErrorCodes.NotFound, "Чек не найден"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ReceiptDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    private Guid GetEmployeeId()
    {
        var raw = User.FindFirstValue("employee_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.Parse(raw!);
    }
}

public sealed record RefundReceiptRequest(string? Comment);
