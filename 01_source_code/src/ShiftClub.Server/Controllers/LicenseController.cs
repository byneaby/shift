using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Licensing;
using ShiftClub.Shared.ErrorCodes;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/license")]
public sealed class LicenseController : ControllerBase
{
    private readonly ILicenseService _license;

    public LicenseController(ILicenseService license)
    {
        _license = license;
    }

    /// <summary>Состояние лицензии. Видят все сотрудники — панель показывает предупреждение всем.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<LicenseStatusDto>>> Get(CancellationToken cancellationToken)
    {
        var status = await _license.GetStatusAsync(cancellationToken);
        return Ok(ApiResponse<LicenseStatusDto>.Ok(status));
    }

    [HttpPut]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<LicenseStatusDto>>> Put(
        [FromBody] SetLicenseKeyRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var status = await _license.SetKeyAsync(request.Key, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<LicenseStatusDto>.Ok(status));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<LicenseStatusDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpDelete]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse>> Delete(CancellationToken cancellationToken)
    {
        await _license.RemoveKeyAsync(GetEmployeeId(), cancellationToken);
        return Ok(ApiResponse.Ok());
    }

    private Guid? GetEmployeeId()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(id, out var gid) ? gid : null;
    }
}
