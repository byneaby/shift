using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Infrastructure.SignalR;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.ClientUpdates;
using ShiftClub.Shared.ErrorCodes;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Route("api/client/updates")]
public sealed class ClientUpdatesController : ControllerBase
{
    private readonly IClientUpdateService _updates;

    public ClientUpdatesController(IClientUpdateService updates)
    {
        _updates = updates;
    }

    [HttpGet("check")]
    [Authorize(AuthenticationSchemes = DeviceAuthDefaults.SchemeName)]
    public async Task<ActionResult<ApiResponse<ClientUpdateCheckResponse>>> Check(
        [FromQuery] string? currentVersion,
        CancellationToken cancellationToken)
    {
        var result = await _updates.CheckAsync(currentVersion, cancellationToken);
        return Ok(ApiResponse<ClientUpdateCheckResponse>.Ok(result));
    }

    [HttpGet("package")]
    [Authorize(AuthenticationSchemes = DeviceAuthDefaults.SchemeName)]
    public async Task<IActionResult> Download(
        [FromQuery] string? version,
        CancellationToken cancellationToken)
    {
        var package = await _updates.OpenPackageAsync(version, cancellationToken);
        if (package is null)
            return NotFound(ApiResponse.Fail(CommonErrorCodes.NotFound, "Пакет обновления не найден"));

        return File(package.Value.Stream, package.Value.ContentType, package.Value.FileName);
    }
}

[ApiController]
[Authorize]
[Route("api/client-updates")]
public sealed class ClientUpdatesAdminController : ControllerBase
{
    private readonly IClientUpdateService _updates;

    public ClientUpdatesAdminController(IClientUpdateService updates)
    {
        _updates = updates;
    }

    [HttpGet("current")]
    public async Task<ActionResult<ApiResponse<ClientUpdateManifestDto?>>> Current(CancellationToken cancellationToken)
    {
        var manifest = await _updates.GetCurrentManifestAsync(cancellationToken);
        return Ok(ApiResponse<ClientUpdateManifestDto?>.Ok(manifest));
    }

    [HttpPost("publish")]
    [RequestSizeLimit(200_000_000)]
    public async Task<ActionResult<ApiResponse<ClientUpdateManifestDto>>> Publish(
        IFormFile package,
        [FromForm] string version,
        [FromForm] string? releaseNotes,
        [FromForm] string? channel,
        CancellationToken cancellationToken)
    {
        if (package.Length == 0)
            return BadRequest(ApiResponse<ClientUpdateManifestDto>.Fail(CommonErrorCodes.ValidationFailed, "Пустой файл"));

        try
        {
            await using var stream = package.OpenReadStream();
            var result = await _updates.PublishAsync(stream, version, releaseNotes, channel, cancellationToken);
            return Ok(ApiResponse<ClientUpdateManifestDto>.Ok(result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ClientUpdateManifestDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }
}
