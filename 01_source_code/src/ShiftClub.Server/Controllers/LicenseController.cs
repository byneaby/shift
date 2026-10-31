using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Server.Licensing;
using ShiftClub.Shared.Contracts;

namespace ShiftClub.Server.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/license")]
public class LicenseController : ControllerBase
{
    private readonly LicenseState _state;
    private readonly ShiftClubDbContext _db;
    private readonly IOptions<LicenseOptions> _options;

    public LicenseController(LicenseState state, ShiftClubDbContext db, IOptions<LicenseOptions> options)
    {
        _state = state;
        _db = db;
        _options = options;
    }

    [HttpGet("status")]
    public async Task<ActionResult<ApiResponse<LicenseStatusDto>>> Status(CancellationToken cancellationToken)
    {
        var used = await _db.Computers.CountAsync(c => c.IsApproved && !c.IsDeleted, cancellationToken);
        var snap = _state.Current;
        var refresh = Math.Clamp(_options.Value.RefreshMinutes, 1, 180);
        return Ok(ApiResponse<LicenseStatusDto>.Ok(new LicenseStatusDto(
            snap.Enforce,
            snap.Operational,
            snap.Message,
            snap.Club,
            snap.Kind,
            snap.MaxComputers,
            used,
            snap.ExpiresAt,
            snap.DaysRemaining,
            snap.Warning,
            refresh)));
    }

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
