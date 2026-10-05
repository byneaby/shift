using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Import;
using ShiftClub.Shared.ErrorCodes;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/customers/import")]
public sealed class CustomerImportController : ControllerBase
{
    private const int MaxCsvLength = 8_000_000;

    private readonly ICustomerImportService _import;
    private readonly ShiftClubDbContext _db;

    public CustomerImportController(ICustomerImportService import, ShiftClubDbContext db)
    {
        _import = import;
        _db = db;
    }

    /// <summary>Разбор файла без записи в базу — чтобы было видно, что получится.</summary>
    [HttpPost("preview")]
    [RequirePermission(PermissionCodes.CustomersManage, PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<CustomerImportPreviewDto>>> Preview(
        [FromBody] CustomerImportRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Csv?.Length > MaxCsvLength)
        {
            return BadRequest(ApiResponse<CustomerImportPreviewDto>.Fail(
                CommonErrorCodes.ValidationFailed,
                "Файл слишком большой. Разбейте выгрузку на части."));
        }

        var branchId = await ResolveBranchIdAsync(cancellationToken);
        var dto = await _import.PreviewAsync(request, branchId, cancellationToken);
        return Ok(ApiResponse<CustomerImportPreviewDto>.Ok(dto));
    }

    /// <summary>Сам импорт. Доступен только владельцу: это разовая операция с деньгами клиентов.</summary>
    [HttpPost]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<CustomerImportResultDto>>> Import(
        [FromBody] CustomerImportRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Csv?.Length > MaxCsvLength)
        {
            return BadRequest(ApiResponse<CustomerImportResultDto>.Fail(
                CommonErrorCodes.ValidationFailed,
                "Файл слишком большой. Разбейте выгрузку на части."));
        }

        try
        {
            var branchId = await ResolveBranchIdAsync(cancellationToken);
            var dto = await _import.ImportAsync(request, branchId, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<CustomerImportResultDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CustomerImportResultDto>.Fail(
                CommonErrorCodes.ValidationFailed,
                ex.Message));
        }
    }

    private async Task<Guid> ResolveBranchIdAsync(CancellationToken cancellationToken)
    {
        var fromClaim = User.FindFirstValue("branch_id");
        if (Guid.TryParse(fromClaim, out var branchId))
            return branchId;

        return await _db.Branches.AsNoTracking()
            .OrderBy(b => b.CreatedAt)
            .Select(b => b.Id)
            .FirstAsync(cancellationToken);
    }

    private Guid GetEmployeeId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
