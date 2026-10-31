using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Server.Licensing;
using ShiftClub.Shared.Contracts;

namespace ShiftClub.Server.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/license")]
public class LicenseController : ControllerBase
{
    private readonly LicenseState _state;

    public LicenseController(LicenseState state)
    {
        _state = state;
    }

    [HttpGet("status")]
    public ActionResult<ApiResponse<LicenseSnapshot>> Status() =>
        Ok(ApiResponse<LicenseSnapshot>.Ok(_state.Current));

    [HttpPost("activate")]
    public async Task<ActionResult<ApiResponse>> Activate(
        [FromBody] ActivateLicenseRequest request,
        CancellationToken cancellationToken)
    {
        if (_state.Current.Operational && User.Identity?.IsAuthenticated != true)
            return Unauthorized(ApiResponse.Fail("unauthorized", "Чтобы сменить ключ на работающем сервере, войдите в панель."));

        var (ok, message) = await _state.ActivateAsync(request.Key, cancellationToken);
        if (!ok)
            return BadRequest(ApiResponse.Fail("license_rejected", message));
        return Ok(ApiResponse.Ok(message));
    }
}

public sealed record ActivateLicenseRequest(string? Key);
