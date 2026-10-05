using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Sessions;
using ShiftClub.Shared.ErrorCodes;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class SessionsController : ControllerBase
{
    private readonly ISessionService _sessions;

    public SessionsController(ISessionService sessions)
    {
        _sessions = sessions;
    }

    [HttpPost("guest")]
    public async Task<ActionResult<ApiResponse<SessionDto>>> StartGuest(
        [FromBody] StartGuestSessionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sessions.StartGuestSessionAsync(request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<SessionDto>.Ok(result));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<SessionDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<SessionDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("guest/batch")]
    public async Task<ActionResult<ApiResponse<StartGuestSessionsBatchResult>>> StartGuestBatch(
        [FromBody] StartGuestSessionsBatchRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sessions.StartGuestSessionsBatchAsync(request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<StartGuestSessionsBatchResult>.Ok(result));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<StartGuestSessionsBatchResult>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<StartGuestSessionsBatchResult>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("{id:guid}/extend")]
    public async Task<ActionResult<ApiResponse<SessionDto>>> Extend(
        Guid id,
        [FromBody] ExtendSessionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sessions.ExtendAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<SessionDto>.Ok(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<SessionDto>.Fail(CommonErrorCodes.NotFound, "Сеанс не найден"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<SessionDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpGet("{id:guid}/extend-quote")]
    public async Task<ActionResult<ApiResponse<ExtendSessionQuoteDto>>> ExtendQuote(
        Guid id,
        [FromQuery] int minutes = 0,
        [FromQuery] Guid? tariffId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _sessions.QuoteExtendAsync(id, minutes, cancellationToken, tariffId);
            return Ok(ApiResponse<ExtendSessionQuoteDto>.Ok(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ExtendSessionQuoteDto>.Fail(CommonErrorCodes.NotFound, "Сеанс не найден"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ExtendSessionQuoteDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("{id:guid}/end")]
    public async Task<ActionResult<ApiResponse<SessionDto>>> End(
        Guid id,
        [FromBody] EndSessionRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sessions.EndAsync(id, request ?? new EndSessionRequest(), GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<SessionDto>.Ok(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<SessionDto>.Fail(CommonErrorCodes.NotFound, "Сеанс не найден"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<SessionDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(ApiResponse<SessionDto>.Fail(CommonErrorCodes.Conflict,
                "Данные изменились параллельно. Обновите карту и повторите завершение."));
        }
    }

    [HttpPost("{id:guid}/transfer")]
    public async Task<ActionResult<ApiResponse<SessionDto>>> Transfer(
        Guid id,
        [FromBody] TransferSessionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sessions.TransferAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<SessionDto>.Ok(result));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<SessionDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<SessionDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(ApiResponse<SessionDto>.Fail(CommonErrorCodes.Conflict,
                "Не удалось перенести сеанс. Обновите карту и повторите."));
        }
    }

    [HttpPost("{id:guid}/pause")]
    public async Task<ActionResult<ApiResponse<SessionDto>>> Pause(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sessions.PauseAsync(id, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<SessionDto>.Ok(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<SessionDto>.Fail(CommonErrorCodes.NotFound, "Сеанс не найден"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<SessionDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("{id:guid}/resume")]
    public async Task<ActionResult<ApiResponse<SessionDto>>> Resume(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sessions.ResumeAsync(id, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<SessionDto>.Ok(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<SessionDto>.Fail(CommonErrorCodes.NotFound, "Сеанс не найден"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<SessionDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("{id:guid}/attach-customer")]
    public async Task<ActionResult<ApiResponse<SessionDto>>> AttachCustomer(
        Guid id,
        [FromBody] AttachSessionCustomerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sessions.AttachCustomerAsync(id, request.CustomerId, cancellationToken);
            return Ok(ApiResponse<SessionDto>.Ok(result));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<SessionDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<SessionDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<SessionDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var session = await _sessions.GetByIdAsync(id, cancellationToken);
        if (session is null)
            return NotFound(ApiResponse<SessionDto>.Fail(CommonErrorCodes.NotFound, "Сеанс не найден"));
        return Ok(ApiResponse<SessionDto>.Ok(session));
    }

    [HttpGet("by-computer/{computerId:guid}")]
    public async Task<ActionResult<ApiResponse<SessionDto>>> GetByComputer(Guid computerId, CancellationToken cancellationToken)
    {
        var session = await _sessions.GetActiveByComputerAsync(computerId, cancellationToken);
        if (session is null)
            return NotFound(ApiResponse<SessionDto>.Fail(CommonErrorCodes.NotFound, "Активный сеанс не найден"));
        return Ok(ApiResponse<SessionDto>.Ok(session));
    }

    private Guid GetEmployeeId()
    {
        var raw = User.FindFirstValue("employee_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.Parse(raw!);
    }
}
