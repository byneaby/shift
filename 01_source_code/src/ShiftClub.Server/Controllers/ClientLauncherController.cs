using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Infrastructure.SignalR;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.ClientLauncher;
using ShiftClub.Shared.Contracts.Customers;
using ShiftClub.Shared.Contracts.Sessions;
using ShiftClub.Shared.Contracts.Settings;
using ShiftClub.Shared.ErrorCodes;
using ShiftClub.Shared.SignalR;
using System.Security.Claims;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Route("api/client")]
[Authorize(AuthenticationSchemes = DeviceAuthDefaults.SchemeName)]
public sealed class ClientLauncherController : ControllerBase
{
    private readonly IClientLauncherService _launcher;
    private readonly ISessionService _sessions;
    private readonly ShiftClubDbContext _db;
    private readonly IHubContext<StaffHub> _staffHub;
    private readonly IClubSettingsService _settings;
    private readonly ITelegramAlertSink _telegramAlerts;
    private readonly IBrandingService _branding;

    public ClientLauncherController(
        IClientLauncherService launcher,
        ISessionService sessions,
        ShiftClubDbContext db,
        IHubContext<StaffHub> staffHub,
        IClubSettingsService settings,
        ITelegramAlertSink telegramAlerts,
        IBrandingService branding)
    {
        _launcher = launcher;
        _sessions = sessions;
        _db = db;
        _staffHub = staffHub;
        _settings = settings;
        _telegramAlerts = telegramAlerts;
        _branding = branding;
    }

    /// <summary>Public branding for login wallpaper (available before customer login).</summary>
    [HttpGet("branding")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<ClientBrandingDto>>> Branding(CancellationToken cancellationToken)
    {
        var bg = await _settings.GetLoginBackgroundAsync(cancellationToken);
        var brand = await _branding.GetAsync(cancellationToken);
        return Ok(ApiResponse<ClientBrandingDto>.Ok(new ClientBrandingDto(
            bg.Url,
            brand.ClubName,
            brand.ShortName,
            brand.LogoUrl,
            brand.AccentColor,
            brand.SupportContact)));
    }

    [HttpPost("auth/login")]
    public async Task<ActionResult<ApiResponse<ClientCustomerAuthDto>>> Login(
        [FromBody] ClientCustomerLoginRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _launcher.LoginAsync(GetComputerId(), request, cancellationToken);
            return Ok(ApiResponse<ClientCustomerAuthDto>.Ok(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ClientCustomerAuthDto>.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ClientCustomerAuthDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("auth/telegram/start")]
    public async Task<ActionResult<ApiResponse<ClientTelegramTicketDto>>> TelegramStart(
        [FromBody] ClientTelegramTicketRequest request,
        [FromServices] ITelegramAuthService telegramAuth,
        CancellationToken cancellationToken)
    {
        try
        {
            // Link/Change — только через authenticated endpoints ниже
            var purpose = (request.Purpose ?? "Login").Trim();
            if (purpose is "LinkAccount" or "ChangeTelegram" or "link" or "change" or "change-telegram")
            {
                return BadRequest(ApiResponse<ClientTelegramTicketDto>.Fail(
                    CommonErrorCodes.ValidationFailed,
                    "Привязка и смена Telegram доступны из профиля аккаунта"));
            }

            var ticket = await telegramAuth.CreateTicketAsync(
                GetComputerId(),
                purpose,
                request.SessionId,
                customerId: null,
                cancellationToken);
            return Ok(ApiResponse<ClientTelegramTicketDto>.Ok(ticket));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ClientTelegramTicketDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ClientTelegramTicketDto>.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
    }

    [HttpPost("auth/telegram/link")]
    public async Task<ActionResult<ApiResponse<ClientTelegramTicketDto>>> TelegramLink(
        [FromServices] ITelegramAuthService telegramAuth,
        CancellationToken cancellationToken)
    {
        var (computerId, customerId, _, fail) = await RequireCustomerAsync<ClientTelegramTicketDto>(cancellationToken);
        if (fail is not null) return fail;

        try
        {
            var ticket = await telegramAuth.CreateTicketAsync(
                computerId, "LinkAccount", sessionId: null, customerId: customerId, cancellationToken);
            return Ok(ApiResponse<ClientTelegramTicketDto>.Ok(ticket));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ClientTelegramTicketDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("auth/telegram/change")]
    public async Task<ActionResult<ApiResponse<ClientTelegramTicketDto>>> TelegramChange(
        [FromServices] ITelegramAuthService telegramAuth,
        CancellationToken cancellationToken)
    {
        var (computerId, customerId, _, fail) = await RequireCustomerAsync<ClientTelegramTicketDto>(cancellationToken);
        if (fail is not null) return fail;

        try
        {
            var ticket = await telegramAuth.CreateTicketAsync(
                computerId, "ChangeTelegram", sessionId: null, customerId: customerId, cancellationToken);
            return Ok(ApiResponse<ClientTelegramTicketDto>.Ok(ticket));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ClientTelegramTicketDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpGet("auth/telegram/status/{ticketId:guid}")]
    public async Task<ActionResult<ApiResponse<ClientTelegramTicketStatusDto>>> TelegramStatus(
        Guid ticketId,
        [FromServices] ITelegramAuthService telegramAuth,
        CancellationToken cancellationToken)
    {
        try
        {
            var status = await telegramAuth.GetTicketStatusAsync(GetComputerId(), ticketId, cancellationToken);
            return Ok(ApiResponse<ClientTelegramTicketStatusDto>.Ok(status));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ClientTelegramTicketStatusDto>.Fail(CommonErrorCodes.NotFound, "Код не найден"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ClientTelegramTicketStatusDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("auth/telegram/cancel/{ticketId:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> TelegramCancel(
        Guid ticketId,
        [FromServices] ITelegramAuthService telegramAuth,
        CancellationToken cancellationToken)
    {
        await telegramAuth.CancelTicketAsync(ticketId, clientNonce: null, cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { }));
    }

    [HttpPost("auth/telegram/complete-registration/{ticketId:guid}")]
    public async Task<ActionResult<ApiResponse<ClientTelegramTicketStatusDto>>> TelegramCompleteRegistration(
        Guid ticketId,
        [FromBody] ClientTelegramCompleteRegistrationRequest request,
        [FromServices] ITelegramAuthService telegramAuth,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await telegramAuth.CompleteRegistrationOnPcAsync(
                GetComputerId(), ticketId, request, cancellationToken);
            return Ok(ApiResponse<ClientTelegramTicketStatusDto>.Ok(dto));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ClientTelegramTicketStatusDto>.Fail(CommonErrorCodes.NotFound, "Код не найден"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ClientTelegramTicketStatusDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPut("account/comfort")]
    public async Task<ActionResult<ApiResponse<ClientCustomerAuthDto>>> UpdateComfort(
        [FromBody] ClientComfortSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var (computerId, customerId, token, fail) = await RequireCustomerAsync<ClientCustomerAuthDto>(cancellationToken);
        if (fail is not null) return fail;

        try
        {
            var result = await _launcher.UpdateComfortAsync(
                computerId, customerId!.Value, token!, request, cancellationToken);
            return Ok(ApiResponse<ClientCustomerAuthDto>.Ok(result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ClientCustomerAuthDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ClientCustomerAuthDto>.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
    }

    [HttpPost("auth/logout")]
    public async Task<ActionResult<ApiResponse<object>>> Logout(CancellationToken cancellationToken)
    {
        var computerId = GetComputerId();
        var customerToken = Request.Headers["X-Customer-Token"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(customerToken))
            await _launcher.LogoutAsync(computerId, customerToken, cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { }));
    }

    [HttpGet("session/current")]
    public async Task<ActionResult<ApiResponse<SessionDto?>>> CurrentSession(CancellationToken cancellationToken)
    {
        var session = await _launcher.GetCurrentSessionAsync(GetComputerId(), cancellationToken);
        return Ok(ApiResponse<SessionDto?>.Ok(session));
    }

    [HttpGet("tariffs")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TariffDto>>>> Tariffs(CancellationToken cancellationToken)
    {
        var computerId = GetComputerId();
        var zoneId = await _db.Computers.AsNoTracking()
            .Where(c => c.Id == computerId)
            .Select(c => c.ZoneId)
            .FirstOrDefaultAsync(cancellationToken);
        var list = await _sessions.GetTariffsAsync(null, includeInactive: false, onlyAvailableNow: true, zoneId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<TariffDto>>.Ok(list));
    }

    [HttpGet("booking-hold")]
    public async Task<ActionResult<ApiResponse<ClientBookingHoldDto?>>> BookingHold(
        CancellationToken cancellationToken)
    {
        var computerId = GetComputerId();
        var customerToken = Request.Headers["X-Customer-Token"].FirstOrDefault();
        Guid? customerId = null;
        if (!string.IsNullOrWhiteSpace(customerToken))
            customerId = await _launcher.ValidateCustomerTokenAsync(customerToken, computerId, cancellationToken);

        var hold = await _launcher.GetBookingHoldAsync(computerId, customerId, cancellationToken);
        return Ok(ApiResponse<ClientBookingHoldDto?>.Ok(hold));
    }

    [HttpPost("sessions/start")]
    public async Task<ActionResult<ApiResponse<SessionDto>>> StartSession(
        [FromBody] ClientStartSessionRequest request,
        CancellationToken cancellationToken)
    {
        var computerId = GetComputerId();
        var customerToken = Request.Headers["X-Customer-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(customerToken))
            return Unauthorized(ApiResponse<SessionDto>.Fail(CommonErrorCodes.Unauthorized, "Нужна авторизация клиента"));

        var customerId = await _launcher.ValidateCustomerTokenAsync(customerToken, computerId, cancellationToken);
        if (customerId is null)
            return Unauthorized(ApiResponse<SessionDto>.Fail(CommonErrorCodes.Unauthorized, "Токен клиента недействителен"));

        try
        {
            var session = await _launcher.StartBalanceSessionAsync(computerId, customerId.Value, request, cancellationToken);
            return Ok(ApiResponse<SessionDto>.Ok(session));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<SessionDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("sessions/extend")]
    public async Task<ActionResult<ApiResponse<SessionDto>>> ExtendSession(
        [FromBody] ClientExtendSessionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await _launcher.ExtendActiveSessionAsync(GetComputerId(), request, cancellationToken);
            return Ok(ApiResponse<SessionDto>.Ok(session));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<SessionDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<SessionDto>.Fail(CommonErrorCodes.NotFound, "Сеанс не найден"));
        }
    }

    [HttpPost("help")]
    public async Task<ActionResult<ApiResponse>> Help(
        [FromBody] ClientHelpRequest? request,
        CancellationToken cancellationToken)
    {
        var computerId = GetComputerId();
        var computer = await _db.Computers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken);

        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.ComputerHelpRequested,
            new
            {
                computerId,
                computerName = computer?.DisplayName ?? computer?.WindowsName ?? "ПК",
                message = string.IsNullOrWhiteSpace(request?.Message)
                    ? "Гость зовёт администратора"
                    : request!.Message!.Trim(),
                at = DateTimeOffset.UtcNow
            },
            cancellationToken);

        var helpPc = computer?.DisplayName ?? computer?.WindowsName ?? "ПК";
        var helpMsg = string.IsNullOrWhiteSpace(request?.Message)
            ? "Гость зовёт администратора"
            : request!.Message!.Trim();
        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.StaffToast,
            new
            {
                kind = "help",
                title = "Вызов администратора",
                body = $"{helpPc}: {helpMsg}",
                sound = true,
                sticky = true,
                computerId,
                at = DateTimeOffset.UtcNow
            },
            cancellationToken);

        await _telegramAlerts.PublishAsync(
            new StaffAlertMessage(
                "help",
                "Вызов администратора",
                $"{helpPc}: {helpMsg}",
                computerId,
                helpPc),
            cancellationToken);

        return Ok(ApiResponse.Ok("Вызов отправлен"));
    }

    /// <summary>Unlock Shell admin mode (image / CCBoot superclient). Device auth required.</summary>
    [HttpPost("admin/unlock")]
    public async Task<ActionResult<ApiResponse<object>>> AdminUnlock(
        [FromBody] VerifyShellAdminPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var ok = await _settings.VerifyShellAdminPasswordAsync(request.Password ?? "", cancellationToken);
        if (!ok)
            return Unauthorized(ApiResponse<object>.Fail(CommonErrorCodes.Unauthorized, "Неверный пароль админ-режима."));

        return Ok(ApiResponse<object>.Ok(new { unlocked = true }));
    }

    [HttpPost("security-alert")]
    public async Task<ActionResult<ApiResponse>> SecurityAlert(
        [FromBody] ClientSecurityAlertRequest? request,
        CancellationToken cancellationToken)
    {
        var computerId = GetComputerId();
        var computer = await _db.Computers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken);

        var reason = string.IsNullOrWhiteSpace(request?.Reason)
            ? "Попытка обойти оболочку"
            : request!.Reason!.Trim();
        var detail = string.IsNullOrWhiteSpace(request?.Detail) ? null : request!.Detail!.Trim();
        var computerName = computer?.DisplayName ?? computer?.WindowsName ?? "ПК";
        var message = detail is null ? reason : $"{reason}: {detail}";

        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.ComputerSecurityAlert,
            new
            {
                computerId,
                computerName,
                reason,
                detail,
                message,
                at = DateTimeOffset.UtcNow
            },
            cancellationToken);

        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.StaffToast,
            new
            {
                kind = "security",
                title = "Безопасность",
                body = $"{computerName}: {message}",
                sound = true,
                sticky = true,
                computerId,
                at = DateTimeOffset.UtcNow
            },
            cancellationToken);

        await _telegramAlerts.PublishAsync(
            new StaffAlertMessage(
                "security",
                "Безопасность",
                $"{computerName}: {message}",
                computerId,
                computerName),
            cancellationToken);

        return Ok(ApiResponse.Ok("Оповещение отправлено"));
    }

    [HttpGet("apps")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SoftwareAppDto>>>> Apps(CancellationToken cancellationToken)
    {
        var list = await _launcher.GetAppsAsync(GetComputerId(), cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<SoftwareAppDto>>.Ok(list));
    }

    [HttpGet("news")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ShiftClub.Shared.Contracts.News.ClientNewsDto>>>> News(
        CancellationToken cancellationToken)
    {
        var list = await _launcher.GetNewsAsync(GetComputerId(), cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ShiftClub.Shared.Contracts.News.ClientNewsDto>>.Ok(list));
    }

    [HttpGet("account")]
    public async Task<ActionResult<ApiResponse<ClientCustomerAuthDto>>> Account(CancellationToken cancellationToken)
    {
        var computerId = GetComputerId();
        var customerToken = Request.Headers["X-Customer-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(customerToken))
            return Unauthorized(ApiResponse<ClientCustomerAuthDto>.Fail(CommonErrorCodes.Unauthorized, "Нужна авторизация клиента"));

        var customerId = await _launcher.ValidateCustomerTokenAsync(customerToken, computerId, cancellationToken);
        if (customerId is null)
            return Unauthorized(ApiResponse<ClientCustomerAuthDto>.Fail(CommonErrorCodes.Unauthorized, "Токен недействителен"));

        var account = await _launcher.GetAccountAsync(computerId, customerId.Value, customerToken, cancellationToken);
        if (account is null)
            return Unauthorized(ApiResponse<ClientCustomerAuthDto>.Fail(CommonErrorCodes.Unauthorized, "Сессия входа закрыта (вход на другом ПК?)"));
        return Ok(ApiResponse<ClientCustomerAuthDto>.Ok(account));
    }

    [HttpPost("account/credentials")]
    public async Task<ActionResult<ApiResponse<ClientCustomerAuthDto>>> ChangeCredentials(
        [FromBody] ClientChangeCredentialsRequest request,
        CancellationToken cancellationToken)
    {
        var (computerId, customerId, customerToken, fail) = await RequireCustomerAsync<ClientCustomerAuthDto>(cancellationToken);
        if (fail is not null) return fail;

        try
        {
            var account = await _launcher.ChangeCredentialsAsync(
                computerId, customerId!.Value, customerToken!, request, cancellationToken);
            return Ok(ApiResponse<ClientCustomerAuthDto>.Ok(account));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ApiResponse<ClientCustomerAuthDto>.Fail(CommonErrorCodes.Unauthorized, ex.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ClientCustomerAuthDto>.Fail(CommonErrorCodes.NotFound, "Клиент не найден"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ClientCustomerAuthDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPut("account/profile")]
    public async Task<ActionResult<ApiResponse<ClientCustomerAuthDto>>> UpdateProfile(
        [FromBody] ClientUpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var (computerId, customerId, customerToken, fail) = await RequireCustomerAsync<ClientCustomerAuthDto>(cancellationToken);
        if (fail is not null) return fail;

        try
        {
            var account = await _launcher.UpdateProfileAsync(
                computerId, customerId!.Value, customerToken!, request, cancellationToken);
            return Ok(ApiResponse<ClientCustomerAuthDto>.Ok(account));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ApiResponse<ClientCustomerAuthDto>.Fail(CommonErrorCodes.Unauthorized, ex.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ClientCustomerAuthDto>.Fail(CommonErrorCodes.NotFound, "Клиент не найден"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ClientCustomerAuthDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpGet("account/transactions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CustomerBalanceTransactionDto>>>> AccountTransactions(
        [FromQuery] int take = 30,
        CancellationToken cancellationToken = default)
    {
        var (_, customerId, _, fail) = await RequireCustomerAsync<IReadOnlyList<CustomerBalanceTransactionDto>>(cancellationToken);
        if (fail is not null) return fail;

        var list = await _launcher.GetAccountTransactionsAsync(customerId!.Value, take, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<CustomerBalanceTransactionDto>>.Ok(list));
    }

    [HttpGet("account/time-bank/transactions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CustomerTimeBankTransactionDto>>>> AccountTimeBankTransactions(
        [FromQuery] int take = 30,
        CancellationToken cancellationToken = default)
    {
        var (_, customerId, _, fail) = await RequireCustomerAsync<IReadOnlyList<CustomerTimeBankTransactionDto>>(cancellationToken);
        if (fail is not null) return fail;

        var list = await _launcher.GetAccountTimeBankTransactionsAsync(customerId!.Value, take, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<CustomerTimeBankTransactionDto>>.Ok(list));
    }

    [HttpGet("account/sessions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ClientAccountSessionDto>>>> AccountSessions(
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        var (_, customerId, _, fail) = await RequireCustomerAsync<IReadOnlyList<ClientAccountSessionDto>>(cancellationToken);
        if (fail is not null) return fail;

        var list = await _launcher.GetAccountSessionsAsync(customerId!.Value, take, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ClientAccountSessionDto>>.Ok(list));
    }

    [HttpGet("account/orders")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ClientBarOrderDto>>>> AccountOrders(
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        var (_, customerId, _, fail) = await RequireCustomerAsync<IReadOnlyList<ClientBarOrderDto>>(cancellationToken);
        if (fail is not null) return fail;

        var list = await _launcher.GetAccountOrdersAsync(customerId!.Value, take, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ClientBarOrderDto>>.Ok(list));
    }

    [HttpGet("account/bookings")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ClientAccountBookingDto>>>> AccountBookings(
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        var (_, customerId, _, fail) = await RequireCustomerAsync<IReadOnlyList<ClientAccountBookingDto>>(cancellationToken);
        if (fail is not null) return fail;

        var list = await _launcher.GetAccountBookingsAsync(customerId!.Value, take, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ClientAccountBookingDto>>.Ok(list));
    }

    private async Task<(Guid ComputerId, Guid? CustomerId, string? CustomerToken, ActionResult? Fail)>
        RequireCustomerAsync<T>(CancellationToken cancellationToken)
    {
        var computerId = GetComputerId();
        var customerToken = Request.Headers["X-Customer-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(customerToken))
            return (computerId, null, null,
                Unauthorized(ApiResponse<T>.Fail(CommonErrorCodes.Unauthorized, "Нужна авторизация клиента")));

        var customerId = await _launcher.ValidateCustomerTokenAsync(customerToken, computerId, cancellationToken);
        if (customerId is null)
            return (computerId, null, null,
                Unauthorized(ApiResponse<T>.Fail(CommonErrorCodes.Unauthorized, "Токен недействителен")));

        return (computerId, customerId, customerToken, null);
    }

    [HttpPost("sessions/end")]
    public async Task<ActionResult<ApiResponse<SessionDto>>> EndSession(
        [FromBody] ClientEndSessionRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await _launcher.EndActiveSessionAsync(
                GetComputerId(),
                request ?? new ClientEndSessionRequest(false),
                cancellationToken);
            return Ok(ApiResponse<SessionDto>.Ok(session));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<SessionDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<SessionDto>.Fail(CommonErrorCodes.NotFound, "Сеанс не найден"));
        }
    }

    [HttpGet("bar/catalog")]
    public async Task<ActionResult<ApiResponse<ClientBarCatalogDto>>> BarCatalog(CancellationToken cancellationToken)
    {
        var (categories, products) = await _launcher.GetBarCatalogAsync(GetComputerId(), cancellationToken);
        return Ok(ApiResponse<ClientBarCatalogDto>.Ok(new ClientBarCatalogDto(categories, products)));
    }

    [HttpPost("bar/orders")]
    public async Task<ActionResult<ApiResponse<ClientBarOrderDto>>> PlaceBarOrder(
        [FromBody] ClientPlaceBarOrderRequest request,
        CancellationToken cancellationToken)
    {
        var computerId = GetComputerId();
        Guid? customerId = null;
        var customerToken = Request.Headers["X-Customer-Token"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(customerToken))
            customerId = await _launcher.ValidateCustomerTokenAsync(customerToken, computerId, cancellationToken);

        try
        {
            var order = await _launcher.PlaceBarOrderAsync(computerId, customerId, request, cancellationToken);
            return Ok(ApiResponse<ClientBarOrderDto>.Ok(order));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ClientBarOrderDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ClientBarOrderDto>.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
    }

    private Guid GetComputerId() =>
        Guid.Parse(User.FindFirstValue(DeviceAuthDefaults.ComputerIdClaim)!);
}
