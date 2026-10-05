using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Bookings;
using ShiftClub.Shared.Contracts.Cases;
using ShiftClub.Shared.Contracts.TgWebApp;
using ShiftClub.Shared.ErrorCodes;

namespace ShiftClub.Server.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/tg")]
public sealed class TgWebAppController : ControllerBase
{
    private readonly ITgWebAppService _tg;
    private readonly ICaseService _cases;

    public TgWebAppController(ITgWebAppService tg, ICaseService cases)
    {
        _tg = tg;
        _cases = cases;
    }

    [HttpPost("auth")]
    public async Task<ActionResult<ApiResponse<TgWebAppAuthDto>>> Auth(
        [FromBody] TgWebAppAuthRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _tg.AuthenticateAsync(request.InitData, cancellationToken);
            return Ok(ApiResponse<TgWebAppAuthDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<TgWebAppAuthDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse<TgWebAppAuthDto>>> Register(
        [FromBody] TgWebAppRegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _tg.RegisterAsync(request, cancellationToken);
            return Ok(ApiResponse<TgWebAppAuthDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<TgWebAppAuthDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("link")]
    public async Task<ActionResult<ApiResponse<TgWebAppAuthDto>>> Link(
        [FromBody] TgWebAppLinkRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _tg.LinkExistingAsync(request, cancellationToken);
            return Ok(ApiResponse<TgWebAppAuthDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<TgWebAppAuthDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("qr/confirm")]
    public async Task<ActionResult<ApiResponse<TgWebAppQrConfirmDto>>> ConfirmQr(
        [FromBody] TgWebAppQrConfirmRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var header = Request.Headers["Authorization"].FirstOrDefault();
            var token = header?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
                ? header["Bearer ".Length..].Trim()
                : null;
            var dto = await _tg.ConfirmQrAsync(request, token, cancellationToken);
            return Ok(ApiResponse<TgWebAppQrConfirmDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<TgWebAppQrConfirmDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("qr/register")]
    public async Task<ActionResult<ApiResponse<TgWebAppQrConfirmDto>>> QrRegister(
        [FromBody] TgWebAppQrRegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _tg.CompleteQrRegistrationAsync(request, cancellationToken);
            return Ok(ApiResponse<TgWebAppQrConfirmDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<TgWebAppQrConfirmDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<TgWebAppQrConfirmDto>.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
    }

    [HttpGet("home")]
    public async Task<ActionResult<ApiResponse<TgWebAppHomeDto>>> Home(CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<TgWebAppHomeDto>(requireCustomer: true);
        if (fail is not null) return fail;
        try
        {
            var dto = await _tg.GetHomeAsync(session!.CustomerId!.Value, cancellationToken);
            return Ok(ApiResponse<TgWebAppHomeDto>.Ok(dto));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<TgWebAppHomeDto>.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
    }

    [HttpGet("session")]
    public async Task<ActionResult<ApiResponse<TgWebAppSessionDto?>>> Session(CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<TgWebAppSessionDto?>(requireCustomer: true);
        if (fail is not null) return fail;
        var dto = await _tg.GetActiveSessionAsync(session!.CustomerId!.Value, cancellationToken);
        return Ok(ApiResponse<TgWebAppSessionDto?>.Ok(dto));
    }

    [HttpPost("session/end")]
    public async Task<ActionResult<ApiResponse<object>>> EndSession(
        [FromBody] TgEndSessionRequest? request,
        CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<object>(requireCustomer: true);
        if (fail is not null) return fail;
        try
        {
            await _tg.EndSessionAsync(
                session!.CustomerId!.Value,
                request?.SaveRemainingToTimeBank ?? true,
                cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { ended = true }));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.Fail(CommonErrorCodes.NotFound, "Сеанс не найден"));
        }
    }

    [HttpGet("bar/catalog")]
    public async Task<ActionResult<ApiResponse<TgWebAppBarCatalogDto>>> BarCatalog(CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<TgWebAppBarCatalogDto>(requireCustomer: true);
        if (fail is not null) return fail;
        var dto = await _tg.GetBarCatalogAsync(session!.CustomerId!.Value, cancellationToken);
        return Ok(ApiResponse<TgWebAppBarCatalogDto>.Ok(dto));
    }

    [HttpGet("bar/orders")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TgWebAppOrderDto>>>> BarOrders(CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<IReadOnlyList<TgWebAppOrderDto>>(requireCustomer: true);
        if (fail is not null) return fail;
        var dto = await _tg.GetBarOrdersAsync(session!.CustomerId!.Value, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<TgWebAppOrderDto>>.Ok(dto));
    }

    [HttpPost("bar/orders")]
    public async Task<ActionResult<ApiResponse<TgWebAppOrderDto>>> PlaceOrder(
        [FromBody] TgWebAppPlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<TgWebAppOrderDto>(requireCustomer: true);
        if (fail is not null) return fail;
        try
        {
            var dto = await _tg.PlaceBarOrderAsync(session!.CustomerId!.Value, request, cancellationToken);
            return Ok(ApiResponse<TgWebAppOrderDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<TgWebAppOrderDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPut("comfort")]
    public async Task<ActionResult<ApiResponse<TgWebAppAuthDto>>> Comfort(
        [FromBody] TgWebAppComfortRequest request,
        CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<TgWebAppAuthDto>(requireCustomer: true);
        if (fail is not null) return fail;
        try
        {
            var dto = await _tg.UpdateComfortAsync(session!.CustomerId!.Value, request, cancellationToken);
            return Ok(ApiResponse<TgWebAppAuthDto>.Ok(dto));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<TgWebAppAuthDto>.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
    }

    [HttpPut("profile")]
    public async Task<ActionResult<ApiResponse<TgWebAppAuthDto>>> Profile(
        [FromBody] TgWebAppUpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<TgWebAppAuthDto>(requireCustomer: true);
        if (fail is not null) return fail;
        try
        {
            var dto = await _tg.UpdateProfileAsync(
                session!.CustomerId!.Value,
                session.TelegramUserId,
                request,
                cancellationToken);
            return Ok(ApiResponse<TgWebAppAuthDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<TgWebAppAuthDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<TgWebAppAuthDto>.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
    }

    [HttpGet("staff/home")]
    public async Task<ActionResult<ApiResponse<TgStaffHomeDto>>> StaffHome(CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<TgStaffHomeDto>(requireStaff: true);
        if (fail is not null) return fail;
        try
        {
            var dto = await _tg.GetStaffHomeAsync(session!.TelegramUserId, session.EmployeeId, cancellationToken);
            return Ok(ApiResponse<TgStaffHomeDto>.Ok(dto));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ApiResponse<TgStaffHomeDto>.Fail(CommonErrorCodes.Unauthorized, ex.Message));
        }
    }

    [HttpGet("staff/tariffs")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TgStaffTariffDto>>>> StaffTariffs(
        [FromQuery] Guid? computerId,
        CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<IReadOnlyList<TgStaffTariffDto>>(requireStaff: true);
        if (fail is not null) return fail;
        if (session!.EmployeeId is null)
            return Unauthorized(ApiResponse<IReadOnlyList<TgStaffTariffDto>>.Fail(CommonErrorCodes.Unauthorized, "Нет employeeId"));
        try
        {
            var dto = await _tg.GetStaffTariffsAsync(session.EmployeeId.Value, computerId, cancellationToken);
            return Ok(ApiResponse<IReadOnlyList<TgStaffTariffDto>>.Ok(dto));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ApiResponse<IReadOnlyList<TgStaffTariffDto>>.Fail(CommonErrorCodes.Unauthorized, ex.Message));
        }
    }

    [HttpGet("staff/customers")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TgStaffCustomerHitDto>>>> StaffCustomers(
        [FromQuery] string? q,
        CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<IReadOnlyList<TgStaffCustomerHitDto>>(requireStaff: true);
        if (fail is not null) return fail;
        if (session!.EmployeeId is null)
            return Unauthorized(ApiResponse<IReadOnlyList<TgStaffCustomerHitDto>>.Fail(CommonErrorCodes.Unauthorized, "Нет employeeId"));
        var dto = await _tg.SearchStaffCustomersAsync(session.EmployeeId.Value, q ?? "", cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<TgStaffCustomerHitDto>>.Ok(dto));
    }

    [HttpPost("staff/sessions/start")]
    public async Task<ActionResult<ApiResponse<object>>> StaffStart(
        [FromBody] TgStaffStartSessionRequest request,
        CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<object>(requireStaff: true);
        if (fail is not null) return fail;
        if (session!.EmployeeId is null)
            return Unauthorized(ApiResponse<object>.Fail(CommonErrorCodes.Unauthorized, "Нет employeeId"));
        try
        {
            var dto = await _tg.StaffStartSessionAsync(session.EmployeeId.Value, request, cancellationToken);
            return Ok(ApiResponse<object>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ApiResponse<object>.Fail(CommonErrorCodes.Unauthorized, ex.Message));
        }
    }

    [HttpPost("staff/sessions/{sessionId:guid}/extend")]
    public async Task<ActionResult<ApiResponse<object>>> StaffExtend(
        Guid sessionId,
        [FromBody] TgStaffExtendSessionRequest request,
        CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<object>(requireStaff: true);
        if (fail is not null) return fail;
        if (session!.EmployeeId is null)
            return Unauthorized(ApiResponse<object>.Fail(CommonErrorCodes.Unauthorized, "Нет employeeId"));
        try
        {
            var dto = await _tg.StaffExtendSessionAsync(session.EmployeeId.Value, sessionId, request, cancellationToken);
            return Ok(ApiResponse<object>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.Fail(CommonErrorCodes.NotFound, "Сеанс не найден"));
        }
    }

    [HttpGet("staff/sessions/{sessionId:guid}/extend-quote")]
    public async Task<ActionResult<ApiResponse<object>>> StaffExtendQuote(
        Guid sessionId,
        [FromQuery] int minutes = 60,
        [FromQuery] Guid? tariffId = null,
        CancellationToken cancellationToken = default)
    {
        var (session, fail) = RequireSession<object>(requireStaff: true);
        if (fail is not null) return fail;
        if (session!.EmployeeId is null)
            return Unauthorized(ApiResponse<object>.Fail(CommonErrorCodes.Unauthorized, "Нет employeeId"));
        try
        {
            var dto = await _tg.StaffQuoteExtendAsync(session.EmployeeId.Value, sessionId, minutes, cancellationToken, tariffId);
            return Ok(ApiResponse<object>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("staff/sessions/{sessionId:guid}/end")]
    public async Task<ActionResult<ApiResponse<object>>> StaffEnd(
        Guid sessionId,
        [FromBody] TgStaffEndSessionRequest? request,
        CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<object>(requireStaff: true);
        if (fail is not null) return fail;
        if (session!.EmployeeId is null)
            return Unauthorized(ApiResponse<object>.Fail(CommonErrorCodes.Unauthorized, "Нет employeeId"));
        try
        {
            var dto = await _tg.StaffEndSessionAsync(
                session.EmployeeId.Value,
                sessionId,
                request ?? new TgStaffEndSessionRequest(),
                cancellationToken);
            return Ok(ApiResponse<object>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.Fail(CommonErrorCodes.NotFound, "Сеанс не найден"));
        }
    }

    [HttpPost("staff/sessions/{sessionId:guid}/pause")]
    public async Task<ActionResult<ApiResponse<object>>> StaffPause(Guid sessionId, CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<object>(requireStaff: true);
        if (fail is not null) return fail;
        if (session!.EmployeeId is null)
            return Unauthorized(ApiResponse<object>.Fail(CommonErrorCodes.Unauthorized, "Нет employeeId"));
        try
        {
            var dto = await _tg.StaffPauseSessionAsync(session.EmployeeId.Value, sessionId, cancellationToken);
            return Ok(ApiResponse<object>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("staff/sessions/{sessionId:guid}/resume")]
    public async Task<ActionResult<ApiResponse<object>>> StaffResume(Guid sessionId, CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<object>(requireStaff: true);
        if (fail is not null) return fail;
        if (session!.EmployeeId is null)
            return Unauthorized(ApiResponse<object>.Fail(CommonErrorCodes.Unauthorized, "Нет employeeId"));
        try
        {
            var dto = await _tg.StaffResumeSessionAsync(session.EmployeeId.Value, sessionId, cancellationToken);
            return Ok(ApiResponse<object>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("staff/computers/{computerId:guid}/wake")]
    public async Task<ActionResult<ApiResponse<object>>> StaffWake(Guid computerId, CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<object>(requireStaff: true);
        if (fail is not null) return fail;
        if (session!.EmployeeId is null)
            return Unauthorized(ApiResponse<object>.Fail(CommonErrorCodes.Unauthorized, "Нет employeeId"));
        try
        {
            await _tg.StaffWakeComputerAsync(session.EmployeeId.Value, computerId, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { ok = true }));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.Fail(CommonErrorCodes.NotFound, "ПК не найден"));
        }
    }

    [HttpGet("staff/sessions/{sessionId:guid}/transfer-targets")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TgStaffTransferTargetDto>>>> StaffTransferTargets(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<IReadOnlyList<TgStaffTransferTargetDto>>(requireStaff: true);
        if (fail is not null) return fail;
        if (session!.EmployeeId is null)
            return Unauthorized(ApiResponse<IReadOnlyList<TgStaffTransferTargetDto>>.Fail(CommonErrorCodes.Unauthorized, "Нет employeeId"));
        try
        {
            var dto = await _tg.StaffTransferTargetsAsync(session.EmployeeId.Value, sessionId, cancellationToken);
            return Ok(ApiResponse<IReadOnlyList<TgStaffTransferTargetDto>>.Ok(dto));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<IReadOnlyList<TgStaffTransferTargetDto>>.Fail(CommonErrorCodes.NotFound, "Сеанс не найден"));
        }
    }

    [HttpPost("staff/sessions/{sessionId:guid}/transfer")]
    public async Task<ActionResult<ApiResponse<object>>> StaffTransfer(
        Guid sessionId,
        [FromBody] TgStaffTransferRequest request,
        CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<object>(requireStaff: true);
        if (fail is not null) return fail;
        if (session!.EmployeeId is null)
            return Unauthorized(ApiResponse<object>.Fail(CommonErrorCodes.Unauthorized, "Нет employeeId"));
        try
        {
            var dto = await _tg.StaffTransferSessionAsync(session.EmployeeId.Value, sessionId, request, cancellationToken);
            return Ok(ApiResponse<object>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
    }

    [HttpPost("staff/bookings/{bookingId:guid}/arrived")]
    public async Task<ActionResult<ApiResponse<object>>> StaffBookingArrived(Guid bookingId, CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<object>(requireStaff: true);
        if (fail is not null) return fail;
        if (session!.EmployeeId is null)
            return Unauthorized(ApiResponse<object>.Fail(CommonErrorCodes.Unauthorized, "Нет employeeId"));
        try
        {
            var dto = await _tg.StaffMarkBookingArrivedAsync(session.EmployeeId.Value, bookingId, cancellationToken);
            return Ok(ApiResponse<object>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.Fail(CommonErrorCodes.NotFound, "Бронь не найдена"));
        }
    }

    [HttpPost("staff/bookings/{bookingId:guid}/cancel")]
    public async Task<ActionResult<ApiResponse<object>>> StaffBookingCancel(
        Guid bookingId,
        [FromBody] CancelBookingRequest? request,
        CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<object>(requireStaff: true);
        if (fail is not null) return fail;
        if (session!.EmployeeId is null)
            return Unauthorized(ApiResponse<object>.Fail(CommonErrorCodes.Unauthorized, "Нет employeeId"));
        try
        {
            var dto = await _tg.StaffCancelBookingAsync(
                session.EmployeeId.Value,
                bookingId,
                request?.Reason,
                cancellationToken);
            return Ok(ApiResponse<object>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.Fail(CommonErrorCodes.NotFound, "Бронь не найдена"));
        }
    }

    [HttpGet("floor-map")]
    public async Task<ActionResult<ApiResponse<TgFloorMapDto>>> FloorMap(CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<TgFloorMapDto>();
        if (fail is not null) return fail;
        try
        {
            var dto = await _tg.GetFloorMapAsync(cancellationToken);
            return Ok(ApiResponse<TgFloorMapDto>.Ok(dto));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<TgFloorMapDto>.Fail(CommonErrorCodes.NotFound, "Карта не найдена"));
        }
    }

    [HttpGet("case")]
    public async Task<ActionResult<ApiResponse<CaseMyStateDto>>> CaseState(CancellationToken cancellationToken)
    {
        var (session, fail) = RequireSession<CaseMyStateDto>(requireCustomer: true);
        if (fail is not null) return fail;
        try
        {
            var dto = await _cases.GetMyStateAsync(session!.CustomerId!.Value, cancellationToken);
            return Ok(ApiResponse<CaseMyStateDto>.Ok(dto));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<CaseMyStateDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
    }

    [HttpPost("case/open")]
    public Task<ActionResult<ApiResponse<CaseOpenResultDto>>> CaseOpen(
        [FromBody] CaseOpenRequest? request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<ActionResult<ApiResponse<CaseOpenResultDto>>>(
            BadRequest(ApiResponse<CaseOpenResultDto>.Fail(
                CommonErrorCodes.ValidationFailed,
                "SHIFT CASE открывается только на кассе. Подойди к администратору — ключ уже на аккаунте.")));
    }

    private (TgAccessSession? Session, ActionResult? Fail) RequireSession<T>(
        bool requireCustomer = false,
        bool requireStaff = false)
    {
        var header = Request.Headers["Authorization"].FirstOrDefault()
                     ?? Request.Headers["X-Tg-Token"].FirstOrDefault();
        var token = header?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? header["Bearer ".Length..].Trim()
            : header?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            return (null, Unauthorized(ApiResponse<T>.Fail(CommonErrorCodes.Unauthorized, "Нужна авторизация")));

        var session = _tg.ValidateAccessToken(token);
        if (session is null)
            return (null, Unauthorized(ApiResponse<T>.Fail(CommonErrorCodes.Unauthorized, "Сессия истекла")));

        if (requireStaff && !session.IsStaff)
            return (null, Unauthorized(ApiResponse<T>.Fail(CommonErrorCodes.Unauthorized, "Нет доступа сотрудника")));

        if (requireCustomer && session.CustomerId is null)
            return (null, BadRequest(ApiResponse<T>.Fail(CommonErrorCodes.ValidationFailed, "Сначала откройте клиентский аккаунт")));

        return (session, null);
    }
}
