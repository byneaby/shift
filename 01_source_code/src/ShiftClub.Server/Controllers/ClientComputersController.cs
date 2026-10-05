using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Infrastructure.SignalR;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Computers;
using ShiftClub.Shared.Enums;
using ShiftClub.Shared.ErrorCodes;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Route("api/client/computers")]
public class ClientComputersController : ControllerBase
{
    private readonly IComputerService _computers;

    public ClientComputersController(IComputerService computers)
    {
        _computers = computers;
    }

    /// <summary>
    /// Регистрация / вход по MAC. Approved ПК сразу получает DeviceToken; новый — Pending до approve в зале.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<RegisterComputerResponse>>> Register(
        [FromBody] RegisterComputerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _computers.RegisterAsync(request, null, cancellationToken);
            return Ok(ApiResponse<RegisterComputerResponse>.Ok(result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<RegisterComputerResponse>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("heartbeat")]
    [Authorize(AuthenticationSchemes = DeviceAuthDefaults.SchemeName)]
    public async Task<ActionResult<ApiResponse<ComputerDto>>> Heartbeat(
        [FromBody] ComputerHeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        var computerId = GetComputerId();
        var result = await _computers.HeartbeatAsync(computerId, request, cancellationToken);
        return Ok(ApiResponse<ComputerDto>.Ok(result));
    }

    [HttpGet("commands/pending")]
    [Authorize(AuthenticationSchemes = DeviceAuthDefaults.SchemeName)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ComputerCommandDto>>>> PendingCommands(
        CancellationToken cancellationToken)
    {
        var list = await _computers.GetPendingCommandsAsync(GetComputerId(), cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ComputerCommandDto>>.Ok(list));
    }

    [HttpPost("commands/{commandId:guid}/status")]
    [Authorize(AuthenticationSchemes = DeviceAuthDefaults.SchemeName)]
    public async Task<ActionResult<ApiResponse>> UpdateCommandStatus(
        Guid commandId,
        [FromBody] UpdateCommandStatusRequest request,
        CancellationToken cancellationToken)
    {
        await _computers.MarkCommandStatusAsync(
            commandId,
            GetComputerId(),
            request.Status,
            request.ErrorMessage,
            request.ResultJson,
            cancellationToken);
        return Ok(ApiResponse.Ok());
    }

    private Guid GetComputerId() =>
        Guid.Parse(User.FindFirstValue(DeviceAuthDefaults.ComputerIdClaim)!);
}

public sealed record UpdateCommandStatusRequest(
    ComputerCommandStatus Status,
    string? ErrorMessage,
    string? ResultJson = null);
