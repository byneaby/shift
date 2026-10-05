using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Branding;
using ShiftClub.Shared.ErrorCodes;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Route("api/branding")]
public sealed class BrandingController : ControllerBase
{
    private readonly IBrandingService _branding;

    public BrandingController(IBrandingService branding)
    {
        _branding = branding;
    }

    /// <summary>
    /// Название и цвета клуба. Открыто без входа: это нужно экрану логина,
    /// табло в зале и лендингу — то есть до того, как кто-то вошёл в систему.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<BrandingDto>>> Get(CancellationToken cancellationToken) =>
        Ok(ApiResponse<BrandingDto>.Ok(await _branding.GetAsync(cancellationToken)));

    [HttpPut]
    [Authorize]
    [RequirePermission(PermissionCodes.SettingsManage)]
    public async Task<ActionResult<ApiResponse<BrandingDto>>> Update(
        [FromBody] UpdateBrandingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _branding.UpdateAsync(request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<BrandingDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<BrandingDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    private Guid? GetEmployeeId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
