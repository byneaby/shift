using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Infrastructure.Services;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Cases;
using ShiftClub.Shared.Enums;
using ShiftClub.Shared.ErrorCodes;
using ShiftClub.Shared.Licensing;
using ShiftClub.Shared.Permissions;
using System.Security.Claims;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/cases")]
public sealed class CasesController : ControllerBase
{
    private readonly ICaseService _cases;
    private readonly ICustomerService _customers;
    private readonly DeskDisplayStore _deskDisplay;

    public CasesController(ICaseService cases, ICustomerService customers, DeskDisplayStore deskDisplay)
    {
        _cases = cases;
        _customers = customers;
        _deskDisplay = deskDisplay;
    }

    [HttpGet("admin")]
    [RequirePermission(PermissionCodes.SettingsManage, PermissionCodes.CustomersView)]
    public async Task<ActionResult<ApiResponse<CaseAdminDto>>> Admin(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(ApiResponse<CaseAdminDto>.Ok(await _cases.GetAdminAsync(cancellationToken)));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CaseAdminDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPut("admin")]
    [RequireFeature(LicenseFeatures.ShiftCase)]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<CaseAdminDto>>> UpdateAdmin(
        [FromBody] UpdateCaseAdminRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(ApiResponse<CaseAdminDto>.Ok(await _cases.UpdateAdminAsync(request, cancellationToken)));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CaseAdminDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("admin/prizes")]
    [RequireFeature(LicenseFeatures.ShiftCase)]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<CaseAdminPrizeDto>>> CreatePrize(
        [FromBody] UpsertCasePrizeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _cases.UpsertPrizeAsync(null, request, cancellationToken);
            return Ok(ApiResponse<CaseAdminPrizeDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CaseAdminPrizeDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPut("admin/prizes/{prizeId:guid}")]
    [RequireFeature(LicenseFeatures.ShiftCase)]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<CaseAdminPrizeDto>>> UpdatePrize(
        Guid prizeId,
        [FromBody] UpsertCasePrizeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _cases.UpsertPrizeAsync(prizeId, request, cancellationToken);
            return Ok(ApiResponse<CaseAdminPrizeDto>.Ok(dto));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<CaseAdminPrizeDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CaseAdminPrizeDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("admin/prizes/{prizeId:guid}/active")]
    [RequireFeature(LicenseFeatures.ShiftCase)]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse>> SetPrizeActive(
        Guid prizeId,
        [FromQuery] bool active = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _cases.SetPrizeActiveAsync(prizeId, active, cancellationToken);
            return Ok(ApiResponse.Ok(active ? "Приз включён" : "Приз выключен"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
    }

    [HttpPost("admin/prizes/{prizeId:guid}/image")]
    [RequireFeature(LicenseFeatures.ShiftCase)]
    [RequestSizeLimit(8_000_000)]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<CaseAdminPrizeDto>>> UploadPrizeImage(
        Guid prizeId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
            return BadRequest(ApiResponse<CaseAdminPrizeDto>.Fail(CommonErrorCodes.ValidationFailed, "Файл пустой"));

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".gif"))
            return BadRequest(ApiResponse<CaseAdminPrizeDto>.Fail(CommonErrorCodes.ValidationFailed, "Нужен jpg/png/webp/gif"));

        try
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "data", "case-images");
            Directory.CreateDirectory(dir);
            var name = $"{prizeId:N}{ext}";
            var path = Path.Combine(dir, name);
            await using (var stream = System.IO.File.Create(path))
                await file.CopyToAsync(stream, cancellationToken);

            var url = $"/media/case/{name}?v={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var dto = await _cases.SetPrizeImageUrlAsync(prizeId, url, cancellationToken);
            return Ok(ApiResponse<CaseAdminPrizeDto>.Ok(dto));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<CaseAdminPrizeDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
    }

    [HttpGet("stats")]
    [RequirePermission(PermissionCodes.CustomersView, PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<CaseStatsDto>>> Stats(CancellationToken cancellationToken)
    {
        var dto = await _cases.GetStatsAsync(cancellationToken);
        return Ok(ApiResponse<CaseStatsDto>.Ok(dto));
    }

    /// <summary>Касса: ключи и каталог для гостя перед открытием кейса.</summary>
    [HttpGet("desk/{customerId:guid}")]
    [RequirePermission(PermissionCodes.CustomersManage, PermissionCodes.CustomersView)]
    public async Task<ActionResult<ApiResponse<CaseDeskGuestDto>>> DeskGuest(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await _cases.GetMyStateAsync(customerId, cancellationToken);
            var customer = await _customers.GetByIdAsync(customerId, cancellationToken)
                ?? throw new KeyNotFoundException("Клиент не найден.");
            return Ok(ApiResponse<CaseDeskGuestDto>.Ok(new CaseDeskGuestDto(
                customer.Id,
                customer.FullName,
                customer.Phone,
                state.KeysBalance,
                state.Catalog.KeyCost,
                state.Catalog.IsEnabled)));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<CaseDeskGuestDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CaseDeskGuestDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("seed")]
    [RequireFeature(LicenseFeatures.ShiftCase)]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse>> Seed(CancellationToken cancellationToken)
    {
        await _cases.EnsureSeededAsync(cancellationToken);
        return Ok(ApiResponse.Ok("Каталог SHIFT CASE обновлён из loot JSON"));
    }

    /// <summary>Касса: продать ключи SHIFT CASE гостю.</summary>
    [HttpPost("buy-keys")]
    [RequireFeature(LicenseFeatures.ShiftCase)]
    [RequirePermission(PermissionCodes.CustomersDeposit, PermissionCodes.CustomersManage)]
    public async Task<ActionResult<ApiResponse<BuyCaseKeysResultDto>>> BuyKeys(
        [FromBody] BuyCaseKeysRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _cases.BuyKeysAsync(request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<BuyCaseKeysResultDto>.Ok(dto));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<BuyCaseKeysResultDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<BuyCaseKeysResultDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    /// <summary>Касса: начислить ключи вручную (подарок / компенсация).</summary>
    [HttpPost("grant-keys")]
    [RequireFeature(LicenseFeatures.ShiftCase)]
    [RequirePermission(PermissionCodes.CustomersManage, PermissionCodes.CustomersAdjust)]
    public async Task<ActionResult<ApiResponse>> GrantKeys(
        [FromBody] CaseGrantKeysRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request.Keys <= 0 || request.Keys > 20)
                throw new InvalidOperationException("Ключей: от 1 до 20.");
            if (string.IsNullOrWhiteSpace(request.Comment) || request.Comment.Trim().Length < 3)
                throw new InvalidOperationException("Укажите комментарий (зачем начисление).");

            await _cases.GrantKeysAsync(
                request.CustomerId,
                request.Keys,
                CaseKeyReason.StaffGrant,
                request.Comment.Trim(),
                request.IdempotencyKey ?? $"staff-grant:{request.CustomerId:N}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                GetEmployeeId(),
                cancellationToken: cancellationToken);
            return Ok(ApiResponse.Ok("Ключи начислены"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpGet("pending-claims")]
    [RequirePermission(PermissionCodes.CustomersView, PermissionCodes.CustomersManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CasePendingClaimDto>>>> PendingClaims(
        CancellationToken cancellationToken)
    {
        var list = await _cases.ListPendingClaimsAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<CasePendingClaimDto>>.Ok(list));
    }

    [HttpPost("rewards/{rewardId:guid}/claim")]
    [RequireFeature(LicenseFeatures.ShiftCase)]
    [RequirePermission(PermissionCodes.CustomersManage, PermissionCodes.CustomersView)]
    public async Task<ActionResult<ApiResponse<CaseUserRewardDto>>> ClaimReward(
        Guid rewardId,
        [FromBody] CaseClaimRewardRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _cases.ClaimRewardAsync(
                rewardId,
                GetEmployeeId(),
                request ?? new CaseClaimRewardRequest(null),
                cancellationToken);
            return Ok(ApiResponse<CaseUserRewardDto>.Ok(dto));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<CaseUserRewardDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CaseUserRewardDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("rewards/{rewardId:guid}/apply")]
    [RequirePermission(PermissionCodes.CustomersManage, PermissionCodes.CustomersView)]
    public async Task<ActionResult<ApiResponse<ApplyCaseRewardResultDto>>> ApplyReward(
        Guid rewardId,
        [FromBody] ApplyCaseRewardRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _cases.ApplyRewardAsync(
                rewardId,
                GetEmployeeId(),
                request ?? new ApplyCaseRewardRequest(null, null),
                cancellationToken);
            return Ok(ApiResponse<ApplyCaseRewardResultDto>.Ok(dto));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<ApplyCaseRewardResultDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ApplyCaseRewardResultDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    /// <summary>Касса: отправить гостя на экран акции (второй монитор с /promo/upgrade).</summary>
    [HttpPost("desk-show/{customerId:guid}")]
    [RequireFeature(LicenseFeatures.ShiftCase)]
    [RequirePermission(PermissionCodes.CustomersManage, PermissionCodes.CustomersView)]
    public async Task<ActionResult<ApiResponse<DeskDisplayCommandDto>>> DeskShow(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var customer = await _customers.GetByIdAsync(customerId, cancellationToken)
                ?? throw new KeyNotFoundException("Клиент не найден.");
            var cmd = _deskDisplay.Show(customer.Id, customer.FullName, customer.Phone);
            return Ok(ApiResponse<DeskDisplayCommandDto>.Ok(cmd));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<DeskDisplayCommandDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
    }

    [HttpPost("desk-show/clear")]
    [RequirePermission(PermissionCodes.CustomersManage, PermissionCodes.CustomersView)]
    public ActionResult<ApiResponse> DeskShowClear([FromQuery] Guid? commandId = null)
    {
        _deskDisplay.Clear(commandId);
        return Ok(ApiResponse.Ok("Ок"));
    }

    /// <summary>Касса: результат открытия кейса с экрана акции (после spin).</summary>
    [HttpGet("desk-result")]
    [RequirePermission(PermissionCodes.CustomersManage, PermissionCodes.CustomersView)]
    public ActionResult<ApiResponse<DeskCaseResultDto?>> DeskResult([FromQuery] Guid? commandId = null)
    {
        var result = _deskDisplay.PeekResult(commandId, TimeSpan.FromMinutes(10));
        return Ok(ApiResponse<DeskCaseResultDto?>.Ok(result));
    }

    /// <summary>Касса: открыть кейс гостю (ключ за регистрацию / подарок).</summary>
    [HttpPost("open/{customerId:guid}")]
    [RequireFeature(LicenseFeatures.ShiftCase)]
    [RequirePermission(PermissionCodes.CustomersManage, PermissionCodes.CustomersView)]
    public async Task<ActionResult<ApiResponse<CaseOpenResultDto>>> OpenForCustomer(
        Guid customerId,
        [FromBody] CaseOpenRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _cases.OpenAsync(
                customerId,
                request ?? new CaseOpenRequest(null),
                cancellationToken);
            return Ok(ApiResponse<CaseOpenResultDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CaseOpenResultDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<CaseOpenResultDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
    }

    private Guid GetEmployeeId()
    {
        var raw = User.FindFirstValue("employee_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.Parse(raw!);
    }
}
