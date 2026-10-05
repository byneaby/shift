using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ShiftClub.Application.Abstractions;
using ShiftClub.Application.Time;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Bar;
using ShiftClub.Shared.Contracts.ClientLauncher;
using ShiftClub.Shared.Contracts.Customers;
using ShiftClub.Shared.Contracts.News;
using ShiftClub.Shared.Contracts.Sessions;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Infrastructure.Services;

public sealed class ClientLauncherService : IClientLauncherService
{
    private readonly ShiftClubDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISessionService _sessions;
    private readonly IBarService _bar;
    private readonly ICustomerService _customers;
    private readonly IClubNewsService _news;
    private readonly IConfiguration _configuration;
    private readonly ICustomerEngagementService _engagement;
    private readonly IClubSettingsService _settings;

    public ClientLauncherService(
        ShiftClubDbContext db,
        IPasswordHasher passwordHasher,
        ISessionService sessions,
        IBarService bar,
        ICustomerService customers,
        IClubNewsService news,
        IConfiguration configuration,
        ICustomerEngagementService engagement,
        IClubSettingsService settings)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _sessions = sessions;
        _bar = bar;
        _customers = customers;
        _news = news;
        _configuration = configuration;
        _engagement = engagement;
        _settings = settings;
    }

    public async Task<ClientCustomerAuthDto> LoginAsync(
        Guid computerId,
        ClientCustomerLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var computer = await _db.Computers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken)
            ?? throw new KeyNotFoundException("ПК не найден");

        var login = request.PhoneOrLogin.Trim();
        if (string.IsNullOrWhiteSpace(login))
            throw new InvalidOperationException("Укажите телефон или логин");

        var digits = new string(login.Where(char.IsDigit).ToArray());
        var last10 = digits.Length >= 10 ? digits[^10..] : null;

        var customer = await _db.Customers
            .Include(c => c.LoyaltyLevel)
            .FirstOrDefaultAsync(c =>
                    c.BranchId == computer.BranchId
                    && c.IsActive
                    && (c.Phone == login
                        || c.Phone == digits
                        || (c.Login != null && c.Login == login)
                        || (last10 != null && c.Phone.EndsWith(last10))),
                cancellationToken)
            ?? throw new InvalidOperationException("Клиент не найден");

        if (customer.IsBlocked)
            throw new InvalidOperationException("Аккаунт заблокирован");

        var secret = request.Pin?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Введите ПИН или пароль");

        var hasPassword = !string.IsNullOrWhiteSpace(customer.PasswordHash);
        var hasPin = !string.IsNullOrWhiteSpace(customer.PinHash);

        if (hasPassword || hasPin)
        {
            var okPassword = hasPassword && _passwordHasher.Verify(secret, customer.PasswordHash!);
            var okPin = hasPin && _passwordHasher.Verify(secret, customer.PinHash!);
            if (!okPassword && !okPin)
                throw new InvalidOperationException("Неверный ПИН или пароль");
        }
        else
        {
            // First login: digits-only → PIN, otherwise → password.
            var digitsOnly = secret.Length >= 4 && secret.All(char.IsDigit);
            if (digitsOnly)
            {
                if (secret.Length < 4)
                    throw new InvalidOperationException("Задайте ПИН (минимум 4 цифры)");
                customer.PinHash = _passwordHasher.Hash(secret);
            }
            else
            {
                if (secret.Length < 4)
                    throw new InvalidOperationException("Задайте пароль (минимум 4 символа)");
                customer.PasswordHash = _passwordHasher.Hash(secret);
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        // Need zone for bank mapping
        computer = await _db.Computers.AsNoTracking()
            .Include(c => c.Zone)
            .FirstAsync(c => c.Id == computerId, cancellationToken);

        await EnsureLoginAllowedForBookingAsync(computer, customer, cancellationToken);
        await EnsureSinglePcLoginAsync(customer, computer, cancellationToken);

        var engagementMsg = await ApplyEngagementAsync(customer, computer.ZoneId, cancellationToken);

        var expires = DateTimeOffset.UtcNow.AddHours(12);
        var token = IssueCustomerToken(customer.Id, computerId, customer.LoginEpoch, expires);
        return await MapAuthAsync(customer, computer, token, expires, cancellationToken, engagementMsg);
    }

    public async Task<ClientCustomerAuthDto> LoginByCustomerIdAsync(
        Guid computerId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var computer = await _db.Computers.AsNoTracking()
            .Include(c => c.Zone)
            .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken)
            ?? throw new KeyNotFoundException("ПК не найден");

        var customer = await _db.Customers
            .Include(c => c.LoyaltyLevel)
            .FirstOrDefaultAsync(c => c.Id == customerId && c.BranchId == computer.BranchId && c.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Клиент не найден");

        if (customer.IsBlocked)
            throw new InvalidOperationException("Аккаунт заблокирован");

        await EnsureLoginAllowedForBookingAsync(computer, customer, cancellationToken);

        string? engagementMsg = null;
        if (customer.LoggedInComputerId == computerId && customer.LoginEpoch > 0)
        {
            customer.LoggedInAt = DateTimeOffset.UtcNow;
            customer.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await EnsureSinglePcLoginAsync(customer, computer, cancellationToken);
            engagementMsg = await ApplyEngagementAsync(customer, computer.ZoneId, cancellationToken);
        }

        var expires = DateTimeOffset.UtcNow.AddHours(12);
        var token = IssueCustomerToken(customer.Id, computerId, customer.LoginEpoch, expires);
        return await MapAuthAsync(customer, computer, token, expires, cancellationToken, engagementMsg);
    }

    public async Task<ClientCustomerAuthDto> UpdateComfortAsync(
        Guid computerId,
        Guid customerId,
        string customerToken,
        ClientComfortSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await ValidateCustomerTokenAsync(customerToken, computerId, cancellationToken) != customerId)
            throw new UnauthorizedAccessException("Токен недействителен");

        var customer = await _db.Customers
            .Include(c => c.LoyaltyLevel)
            .FirstOrDefaultAsync(c => c.Id == customerId && c.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("Клиент не найден");

        var lang = (request.ComfortLanguage ?? "ru").Trim().ToLowerInvariant();
        if (lang is not ("ru" or "kk" or "en"))
            lang = "ru";
        var brightness = Math.Clamp(request.ComfortBrightness, 40, 100);

        customer.ComfortHideBalance = request.ComfortHideBalance;
        customer.ComfortSoundEnabled = request.ComfortSoundEnabled;
        customer.ComfortLanguage = lang;
        customer.ComfortBrightness = brightness;
        customer.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var computer = await _db.Computers.AsNoTracking()
            .Include(c => c.Zone)
            .FirstAsync(c => c.Id == computerId, cancellationToken);
        var expires = DateTimeOffset.UtcNow.AddHours(12);
        return await MapAuthAsync(customer, computer, customerToken, expires, cancellationToken);
    }

    private async Task<string?> ApplyEngagementAsync(
        Customer customer,
        Guid? zoneId,
        CancellationToken cancellationToken)
    {
        var tzId = await _db.Branches.AsNoTracking()
            .Where(b => b.Id == customer.BranchId)
            .Select(b => b.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);
        var tz = ShiftClub.Application.Time.BranchTimeZone.Resolve(tzId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);

        var parts = new List<string>();
        // Серия посещений — только при старте сеанса (SessionService), не при логине.
        var bday = await _engagement.TryGrantBirthdayGiftAsync(customer, zoneId, today, cancellationToken);
        if (!string.IsNullOrWhiteSpace(bday))
            parts.Add(bday);

        // Reload balances after gifts
        await _db.Entry(customer).ReloadAsync(cancellationToken);
        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    public async Task LogoutAsync(
        Guid computerId,
        string customerToken,
        CancellationToken cancellationToken = default)
    {
        var customerId = await ValidateCustomerTokenAsync(customerToken, computerId, cancellationToken);
        if (customerId is null)
            return;

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId.Value, cancellationToken);
        if (customer is null)
            return;

        if (customer.LoggedInComputerId == computerId)
        {
            customer.LoggedInComputerId = null;
            customer.LoggedInAt = null;
            customer.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Один аккаунт — один ПК. Если на другом ПК активный/пауза-сеанс — отказ.
    /// Иначе вытесняем предыдущий вход (новый LoginEpoch + ForceCustomerLogout).
    /// </summary>
    private async Task EnsureSinglePcLoginAsync(
        Customer customer,
        Computer computer,
        CancellationToken cancellationToken)
    {
        var otherLive = await _db.GamingSessions.AsNoTracking()
            .Include(s => s.Computer)
            .Where(s => s.CustomerId == customer.Id
                        && s.ComputerId != computer.Id
                        && (s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused))
            .Select(s => new
            {
                s.ComputerId,
                PcName = s.Computer.DisplayName ?? s.Computer.WindowsName
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (otherLive is not null)
        {
            var name = string.IsNullOrWhiteSpace(otherLive.PcName) ? "другом ПК" : otherLive.PcName;
            throw new InvalidOperationException(
                $"Аккаунт уже играет на «{name}». Завершите сеанс там или обратитесь к администратору.");
        }

        var previousPcId = customer.LoggedInComputerId;
        customer.LoggedInComputerId = computer.Id;
        customer.LoggedInAt = DateTimeOffset.UtcNow;
        customer.LoginEpoch = customer.LoginEpoch <= 0 ? 1 : customer.LoginEpoch + 1;
        customer.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        if (previousPcId is Guid prev && prev != computer.Id)
        {
            await EnqueueForceCustomerLogoutAsync(
                prev,
                computer.BranchId,
                $"Вход выполнен на другом ПК ({computer.DisplayName ?? computer.WindowsName}).",
                cancellationToken);
        }
    }

    private async Task EnqueueForceCustomerLogoutAsync(
        Guid targetComputerId,
        Guid branchId,
        string message,
        CancellationToken cancellationToken)
    {
        var cmd = new ComputerCommand
        {
            ComputerId = targetComputerId,
            Type = ComputerCommandType.ForceCustomerLogout,
            Status = ComputerCommandStatus.Created,
            PayloadJson = $"{{\"message\":{System.Text.Json.JsonSerializer.Serialize(message)}}}",
            IdempotencyKey = $"force-logout-{targetComputerId:N}-{DateTimeOffset.UtcNow.Ticks}",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        _db.ComputerCommands.Add(cmd);
        await _db.SaveChangesAsync(cancellationToken);

        cmd.Status = ComputerCommandStatus.Sent;
        cmd.SentAt = DateTimeOffset.UtcNow;
        cmd.AttemptCount = 1;
        await _db.SaveChangesAsync(cancellationToken);
        _ = branchId; // reserved for audit/hub if needed later
    }

    public async Task<ClientBookingHoldDto?> GetBookingHoldAsync(
        Guid computerId,
        Guid? customerId = null,
        CancellationToken cancellationToken = default)
    {
        var computer = await _db.Computers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken);
        if (computer is null)
            return null;

        Customer? customer = null;
        if (customerId.HasValue)
        {
            customer = await _db.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == customerId.Value, cancellationToken);
        }

        var hold = await FindHoldingBookingAsync(computer, cancellationToken);
        if (hold is null)
            return null;

        return new ClientBookingHoldDto(
            hold.Id,
            hold.Number,
            hold.ContactName,
            hold.StartsAt,
            hold.EndsAt,
            IsBookingOwner(hold, customer));
    }

    public async Task<ClientCustomerAuthDto?> GetAccountAsync(
        Guid computerId,
        Guid customerId,
        string customerToken,
        CancellationToken cancellationToken = default)
    {
        if (ValidateCustomerToken(customerToken, computerId) != customerId)
            return null;

        // Epoch / binding check — вытесненный вход
        if (await ValidateCustomerTokenAsync(customerToken, computerId, cancellationToken) is null)
            return null;

        var computer = await _db.Computers.AsNoTracking()
            .Include(c => c.Zone)
            .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken);
        if (computer is null)
            return null;

        var customer = await _db.Customers.AsNoTracking()
            .Include(c => c.LoyaltyLevel)
            .FirstOrDefaultAsync(c => c.Id == customerId && c.IsActive, cancellationToken);
        if (customer is null)
            return null;

        var expires = DateTimeOffset.UtcNow.AddHours(12);
        return await MapAuthAsync(customer, computer, customerToken, expires, cancellationToken);
    }

    public async Task<SessionDto?> GetCurrentSessionAsync(Guid computerId, CancellationToken cancellationToken = default)
        => await _sessions.GetActiveByComputerAsync(computerId, cancellationToken);

    public async Task<SessionDto> StartBalanceSessionAsync(
        Guid computerId,
        Guid customerId,
        ClientStartSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var customer = await _db.Customers.AsNoTracking()
            .FirstAsync(c => c.Id == customerId, cancellationToken);

        var computer = await _db.Computers.AsNoTracking()
            .FirstAsync(c => c.Id == computerId, cancellationToken);

        Guid? bookingId = null;
        var hold = await FindHoldingBookingAsync(computer, cancellationToken);
        if (hold is not null)
        {
            if (!IsBookingOwner(hold, customer))
            {
                throw new InvalidOperationException(
                    FormatBookingBlockMessage(hold));
            }

            bookingId = hold.Id;
        }

        var starterId = await _db.Employees.AsNoTracking()
            .Where(e => e.BranchId == customer.BranchId && e.IsActive)
            .OrderBy(e => e.CreatedAt)
            .Select(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var useBank = request.UseTimeBank;
        return await _sessions.StartGuestSessionAsync(
            new StartGuestSessionRequest(
                computerId,
                request.TariffId,
                request.DurationMinutes,
                useBank ? PaymentMethod.Free : PaymentMethod.Balance,
                $"{customer.FirstName} {customer.LastName}".Trim(),
                customerId,
                request.IdempotencyKey,
                UseTimeBank: useBank,
                BookingId: bookingId),
            starterId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<SoftwareAppDto>> GetAppsAsync(
        Guid computerId,
        CancellationToken cancellationToken = default)
    {
        var computer = await _db.Computers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken)
            ?? throw new KeyNotFoundException("ПК не найден");

        var apps = await _db.SoftwareApps.AsNoTracking()
            .Where(a => a.BranchId == computer.BranchId && a.IsActive)
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.Name)
            .ToListAsync(cancellationToken);

        return apps.Select(a => new SoftwareAppDto(
            a.Id,
            a.Name,
            a.Category,
            a.ExePath,
            a.Arguments,
            a.WorkingDirectory,
            a.SortOrder,
            // Реальная проверка файла — на игровом клиенте.
            true,
            a.IconPath,
            a.LaunchSoundUrl)).ToList();
    }

    public async Task<SessionDto> ExtendActiveSessionAsync(
        Guid computerId,
        ClientExtendSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessions.GetActiveByComputerAsync(computerId, cancellationToken)
                      ?? throw new InvalidOperationException("Нет активного сеанса");

        if (session.CustomerId is null)
            throw new InvalidOperationException("Продление с ПК доступно только для сеанса с аккаунтом. Для кассового сеанса — через кассу.");

        var employeeId = await _db.Employees.AsNoTracking()
            .Where(e => e.BranchId == session.BranchId && e.IsActive)
            .OrderBy(e => e.CreatedAt)
            .Select(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return await _sessions.ExtendAsync(
            session.Id,
            new ExtendSessionRequest(
                request.AdditionalMinutes,
                PaymentMethod.Balance,
                AmountTendered: null,
                request.IdempotencyKey,
                session.CustomerId,
                request.TariffId),
            employeeId,
            cancellationToken);
    }

    public async Task<SessionDto> EndActiveSessionAsync(
        Guid computerId,
        ClientEndSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessions.GetActiveByComputerAsync(computerId, cancellationToken)
                      ?? throw new InvalidOperationException("Нет активного сеанса");

        var employeeId = await _db.Employees.AsNoTracking()
            .Where(e => e.BranchId == session.BranchId && e.IsActive)
            .OrderBy(e => e.CreatedAt)
            .Select(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return await _sessions.EndAsync(
            session.Id,
            new EndSessionRequest(false, null, request.SaveRemainingToTimeBank),
            employeeId,
            cancellationToken);
    }

    public async Task<(IReadOnlyList<ClientBarCategoryDto> Categories, IReadOnlyList<ClientBarProductDto> Products)> GetBarCatalogAsync(
        Guid computerId,
        CancellationToken cancellationToken = default)
    {
        var computer = await _db.Computers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken)
            ?? throw new KeyNotFoundException("ПК не найден");

        var categories = await _bar.GetCategoriesAsync(computer.BranchId, cancellationToken: cancellationToken);
        var products = await _bar.GetProductsAsync(computer.BranchId, null, cancellationToken: cancellationToken);

        return (
            categories.Where(c => c.IsActive).Select(c => new ClientBarCategoryDto(c.Id, c.Name, c.Code)).ToList(),
            products.Where(p => p.IsActive).Select(p => new ClientBarProductDto(
                p.Id,
                p.CategoryId,
                p.CategoryName,
                p.Name,
                p.Sku,
                p.SalePrice,
                p.StockQty,
                p.StockQty > 0,
                p.ImageUrl)).ToList());
    }

    public async Task<ClientBarOrderDto> PlaceBarOrderAsync(
        Guid computerId,
        Guid? customerId,
        ClientPlaceBarOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var lineItems = (request.Items ?? [])
            .Where(i => i.Quantity > 0)
            .Select(i => new CreateBarOrderItemRequest(i.ProductId, i.Quantity))
            .ToList();
        if (lineItems.Count == 0
            && request.ProductId is Guid legacyProductId
            && (request.Quantity ?? 0) > 0)
        {
            lineItems.Add(new CreateBarOrderItemRequest(legacyProductId, request.Quantity!.Value));
        }

        if (lineItems.Count == 0)
            throw new InvalidOperationException("Корзина пуста");

        var session = await _sessions.GetActiveByComputerAsync(computerId, cancellationToken);
        // Заказ с ПК: нужна активная сессия (кассовый гость или аккаунт)
        if (session is null)
            throw new InvalidOperationException("Заказ из бара доступен во время сеанса");

        // С ПК бар оплачивается только при выдаче: наличными или Kaspi QR.
        var mode = ParsePaymentMode(request.PaymentMode);
        if (mode is not (BarOrderPaymentMode.CashOnDelivery
            or BarOrderPaymentMode.KaspiQrOnDelivery
            or BarOrderPaymentMode.PayAtCashier))
            throw new InvalidOperationException("Заказ с ПК оплачивается только наличными или Kaspi QR при выдаче");

        var employeeId = await _db.Employees.AsNoTracking()
            .Where(e => e.IsActive)
            .OrderBy(e => e.CreatedAt)
            .Select(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var order = await _bar.CreateOrderAsync(
            new CreateBarOrderRequest(
                computerId,
                session?.Id,
                mode,
                "Заказ с клиентского лаунчера",
                request.IdempotencyKey ?? Guid.NewGuid().ToString("N"),
                lineItems),
            employeeId == Guid.Empty ? null : employeeId,
            cancellationToken);

        return new ClientBarOrderDto(
            order.Id,
            order.Number,
            order.Status.ToString(),
            order.PaymentMode.ToString(),
            order.Total,
            order.CreatedAt);
    }

    public async Task<IReadOnlyList<ClientNewsDto>> GetNewsAsync(
        Guid computerId,
        CancellationToken cancellationToken = default)
    {
        var branchId = await _db.Computers.AsNoTracking()
            .Where(c => c.Id == computerId)
            .Select(c => (Guid?)c.BranchId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await _db.Branches.AsNoTracking().Select(b => b.Id).FirstAsync(cancellationToken);
        return await _news.ListPublishedAsync(branchId, 20, cancellationToken);
    }

    private static BarOrderPaymentMode ParsePaymentMode(string? mode) =>
        (mode ?? "PayAtCashier").Trim().ToLowerInvariant() switch
        {
            "balance" => BarOrderPaymentMode.Balance,
            "chargetosession" or "session" => BarOrderPaymentMode.ChargeToSession,
            "cashondelivery" or "cash" => BarOrderPaymentMode.CashOnDelivery,
            "cardondelivery" or "card" => BarOrderPaymentMode.CardOnDelivery,
            "kaspiqrondelivery" or "kaspiqr" or "kaspi" => BarOrderPaymentMode.KaspiQrOnDelivery,
            _ => BarOrderPaymentMode.PayAtCashier
        };

    private async Task<ClientCustomerAuthDto> MapAuthAsync(
        Customer customer,
        Computer computer,
        string token,
        DateTimeOffset expires,
        CancellationToken cancellationToken,
        string? engagementMessage = null)
    {
        var banks = await _db.CustomerZoneTimeBanks.AsNoTracking()
            .Include(b => b.Zone)
            .Where(b => b.CustomerId == customer.Id && b.Minutes > 0)
            .OrderBy(b => b.Zone.SortOrder)
            .ThenBy(b => b.Zone.Name)
            .Select(b => new ClientZoneTimeBankDto(b.ZoneId, b.Zone.Name, b.Minutes))
            .ToListAsync(cancellationToken);

        var currentZoneId = computer.ZoneId;
        var currentZoneName = computer.Zone?.Name;
        var currentZoneMinutes = currentZoneId is Guid zid
            ? banks.FirstOrDefault(b => b.ZoneId == zid)?.Minutes ?? 0
            : 0;

        var levels = await _db.LoyaltyLevels.AsNoTracking()
            .Where(l => l.BranchId == customer.BranchId && l.IsActive)
            .OrderBy(l => l.MinSpent)
            .ToListAsync(cancellationToken);
        var next = levels.FirstOrDefault(l => l.MinSpent > customer.TotalSpent);
        var goal = next?.MinSpent
                   ?? Math.Max(1, levels.LastOrDefault()?.MinSpent ?? 500);
        var progress = Math.Min(goal, (int)Math.Round(customer.TotalSpent));
        if (next is null && levels.Count > 0)
            progress = goal;

        return new ClientCustomerAuthDto(
            customer.Id,
            $"{customer.FirstName} {customer.LastName}".Trim(),
            customer.Phone,
            customer.Balance,
            customer.BonusBalance,
            customer.LoyaltyLevel?.Name,
            token,
            expires,
            customer.VisitCount,
            customer.TotalSpent,
            banks.Sum(b => b.Minutes),
            progress,
            goal,
            banks,
            currentZoneId,
            currentZoneName,
            currentZoneMinutes,
            customer.Email,
            customer.Login,
            !string.IsNullOrWhiteSpace(customer.PinHash),
            !string.IsNullOrWhiteSpace(customer.PasswordHash),
            customer.FirstName,
            customer.LastName,
            customer.LoyaltyLevel?.BonusPercent ?? 0,
            customer.LoyaltyLevel?.TimeDiscountPercent ?? 0,
            customer.VisitStreakDays,
            customer.PendingBarRewards,
            customer.ComfortHideBalance,
            customer.ComfortSoundEnabled,
            customer.ComfortLanguage,
            customer.ComfortBrightness,
            customer.BirthDate,
            customer.TelegramUserId is not null,
            engagementMessage,
            await ResolveTelegramChangeAvailableAtAsync(customer, cancellationToken));
    }

    private async Task<DateTimeOffset?> ResolveTelegramChangeAvailableAtAsync(
        Customer customer,
        CancellationToken cancellationToken)
    {
        if (customer.TelegramUserId is null)
            return null;
        var eng = await _settings.GetEngagementStoredAsync(cancellationToken);
        var cooldown = Math.Clamp(eng.TelegramChangeCooldownDays, 1, 365);
        var last = customer.TelegramChangedAt ?? customer.TelegramLinkedAt;
        if (last is null)
            return null;
        var next = last.Value.AddDays(cooldown);
        return next > DateTimeOffset.UtcNow ? next : null;
    }

    public async Task<ClientCustomerAuthDto> ChangeCredentialsAsync(
        Guid computerId,
        Guid customerId,
        string customerToken,
        ClientChangeCredentialsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await ValidateCustomerTokenAsync(customerToken, computerId, cancellationToken) != customerId)
            throw new UnauthorizedAccessException("Токен недействителен");

        var customer = await _db.Customers
            .Include(c => c.LoyaltyLevel)
            .FirstOrDefaultAsync(c => c.Id == customerId && c.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("Клиент не найден");

        var secret = request.CurrentSecret?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Введите текущий ПИН или пароль");

        var hasPassword = !string.IsNullOrWhiteSpace(customer.PasswordHash);
        var hasPin = !string.IsNullOrWhiteSpace(customer.PinHash);
        var okPassword = hasPassword && _passwordHasher.Verify(secret, customer.PasswordHash!);
        var okPin = hasPin && _passwordHasher.Verify(secret, customer.PinHash!);
        if (!okPassword && !okPin)
            throw new InvalidOperationException("Неверный текущий ПИН или пароль");

        var changed = false;

        if (!string.IsNullOrWhiteSpace(request.Login))
        {
            var login = request.Login.Trim();
            if (login.Length < 3)
                throw new InvalidOperationException("Логин — минимум 3 символа");
            var taken = await _db.Customers.AsNoTracking()
                .AnyAsync(c => c.Id != customerId && c.Login == login && c.IsActive, cancellationToken);
            if (taken)
                throw new InvalidOperationException("Этот логин уже занят");
            customer.Login = login;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(request.NewPin))
        {
            var pin = request.NewPin.Trim();
            if (pin.Length is < 4 or > 8 || !pin.All(char.IsDigit))
                throw new InvalidOperationException("Новый ПИН — 4–8 цифр");
            customer.PinHash = _passwordHasher.Hash(pin);
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            var pwd = request.NewPassword.Trim();
            if (pwd.Length < 4)
                throw new InvalidOperationException("Новый пароль — минимум 4 символа");
            customer.PasswordHash = _passwordHasher.Hash(pwd);
            changed = true;
        }

        if (!changed)
            throw new InvalidOperationException("Укажите новый ПИН, пароль или логин");

        customer.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var computer = await _db.Computers.AsNoTracking()
            .Include(c => c.Zone)
            .FirstAsync(c => c.Id == computerId, cancellationToken);
        var expires = DateTimeOffset.UtcNow.AddHours(12);
        return await MapAuthAsync(customer, computer, customerToken, expires, cancellationToken);
    }

    public async Task<ClientCustomerAuthDto> UpdateProfileAsync(
        Guid computerId,
        Guid customerId,
        string customerToken,
        ClientUpdateProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await ValidateCustomerTokenAsync(customerToken, computerId, cancellationToken) != customerId)
            throw new UnauthorizedAccessException("Токен недействителен");

        var first = request.FirstName?.Trim() ?? "";
        var last = request.LastName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(first))
            throw new InvalidOperationException("Укажите имя");
        if (first.Length > 80 || last.Length > 80)
            throw new InvalidOperationException("Слишком длинное имя");

        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (email is not null && (email.Length > 120 || !email.Contains('@')))
            throw new InvalidOperationException("Некорректный email");

        var customer = await _db.Customers
            .Include(c => c.LoyaltyLevel)
            .FirstOrDefaultAsync(c => c.Id == customerId && c.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("Клиент не найден");

        customer.FirstName = first;
        customer.LastName = last;
        customer.Email = email;
        customer.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var computer = await _db.Computers.AsNoTracking()
            .Include(c => c.Zone)
            .FirstAsync(c => c.Id == computerId, cancellationToken);
        var expires = DateTimeOffset.UtcNow.AddHours(12);
        return await MapAuthAsync(customer, computer, customerToken, expires, cancellationToken);
    }

    public Task<IReadOnlyList<CustomerBalanceTransactionDto>> GetAccountTransactionsAsync(
        Guid customerId,
        int take,
        CancellationToken cancellationToken = default)
        => _customers.GetTransactionsAsync(customerId, Math.Clamp(take, 1, 100), cancellationToken);

    public Task<IReadOnlyList<CustomerTimeBankTransactionDto>> GetAccountTimeBankTransactionsAsync(
        Guid customerId,
        int take,
        CancellationToken cancellationToken = default)
        => _customers.GetTimeBankTransactionsAsync(customerId, Math.Clamp(take, 1, 100), cancellationToken);

    public async Task<IReadOnlyList<ClientAccountSessionDto>> GetAccountSessionsAsync(
        Guid customerId,
        int take,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);
        var rows = await _db.GamingSessions.AsNoTracking()
            .Include(s => s.Computer)
            .Include(s => s.Zone)
            .Include(s => s.Tariff)
            .Where(s => s.CustomerId == customerId
                        && s.Status != SessionStatus.Draft
                        && s.Status != SessionStatus.Cancelled)
            .OrderByDescending(s => s.StartedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        return rows.Select(s => new ClientAccountSessionDto(
            s.Id,
            s.Computer.DisplayName ?? s.Computer.WindowsName,
            s.Zone.Name,
            s.Tariff.Name,
            s.Status.ToString(),
            s.StartedAt,
            s.ActualEndedAt,
            s.DurationMinutes,
            s.TotalPrice,
            s.PaymentMethod.ToString(),
            s.PlannedEndsAt)).ToList();
    }

    public async Task<IReadOnlyList<ClientBarOrderDto>> GetAccountOrdersAsync(
        Guid customerId,
        int take,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);
        var rows = await _db.BarOrders.AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        return rows.Select(o =>
        {
            var names = o.Items
                .Select(i => $"{(string.IsNullOrWhiteSpace(i.ProductName) ? "Позиция" : i.ProductName)} ×{i.Quantity:0.##}")
                .ToList();
            var summary = names.Count == 0
                ? null
                : string.Join(", ", names.Take(4)) + (names.Count > 4 ? "…" : "");
            return new ClientBarOrderDto(
                o.Id,
                o.Number,
                o.Status.ToString(),
                o.PaymentMode.ToString(),
                o.Total,
                o.CreatedAt,
                summary,
                o.Items.Count);
        }).ToList();
    }

    public async Task<IReadOnlyList<ClientAccountBookingDto>> GetAccountBookingsAsync(
        Guid customerId,
        int take,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 50);
        var customer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer is null)
            return [];

        var phoneDigits = new string((customer.Phone ?? "").Where(char.IsDigit).ToArray());
        var phoneTail = phoneDigits.Length >= 10 ? phoneDigits[^10..] : "";

        var byCustomer = await _db.Bookings.AsNoTracking()
            .Include(b => b.Zone)
            .Include(b => b.Computers)
            .ThenInclude(bc => bc.Computer)
            .ThenInclude(c => c!.Zone)
            .Where(b => b.CustomerId == customerId)
            .OrderByDescending(b => b.StartsAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        List<Booking> rows = [.. byCustomer];
        if (phoneTail.Length >= 10 && rows.Count < take)
        {
            var recent = await _db.Bookings.AsNoTracking()
                .Include(b => b.Zone)
                .Include(b => b.Computers)
                .ThenInclude(bc => bc.Computer)
                .ThenInclude(c => c!.Zone)
                .Where(b => b.CustomerId == null && b.ContactPhone != null)
                .OrderByDescending(b => b.StartsAt)
                .Take(100)
                .ToListAsync(cancellationToken);

            foreach (var b in recent)
            {
                if (rows.Count >= take)
                    break;
                var d = new string((b.ContactPhone ?? "").Where(char.IsDigit).ToArray());
                if (d.Length >= 10 && d[^10..] == phoneTail && rows.All(x => x.Id != b.Id))
                    rows.Add(b);
            }

            rows = rows.OrderByDescending(b => b.StartsAt).Take(take).ToList();
        }

        return rows.Select(b =>
        {
            var pcNames = b.Computers
                .Select(bc => bc.Computer?.DisplayName ?? bc.Computer?.WindowsName ?? "?")
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();
            var zone = b.Zone?.Name
                       ?? b.Computers.Select(bc => bc.Computer?.Zone?.Name)
                           .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            return new ClientAccountBookingDto(
                b.Id,
                b.Number,
                b.Status.ToString(),
                b.StartsAt,
                b.EndsAt,
                zone,
                pcNames.Count == 0 ? null : string.Join(", ", pcNames),
                b.PrepaidAmount);
        }).ToList();
    }

    public async Task<Guid?> ValidateCustomerTokenAsync(
        string token,
        Guid computerId,
        CancellationToken cancellationToken = default)
    {
        var parsed = TryParseCustomerToken(token, out var customerId, out var tokenPc, out var epoch);
        if (!parsed || tokenPc != computerId)
            return null;

        var row = await _db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => new { c.LoginEpoch, c.LoggedInComputerId, c.IsActive, c.IsBlocked })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null || !row.IsActive || row.IsBlocked)
            return null;
        if (row.LoggedInComputerId is not null && row.LoggedInComputerId != computerId)
            return null;
        if (epoch != row.LoginEpoch)
            return null;

        return customerId;
    }

    public Guid? ValidateCustomerToken(string token, Guid computerId)
    {
        if (!TryParseCustomerToken(token, out var customerId, out var tokenPc, out _))
            return null;
        return tokenPc == computerId ? customerId : null;
    }

    private bool TryParseCustomerToken(string token, out Guid customerId, out Guid computerId, out int epoch)
    {
        customerId = default;
        computerId = default;
        epoch = 0;
        try
        {
            var key = _configuration["Jwt:SigningKey"]
                      ?? throw new InvalidOperationException("Jwt:SigningKey missing");
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidIssuer = _configuration["Jwt:Issuer"] ?? "ShiftClub",
                ValidAudience = "ShiftClub.ClientCustomer",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out _);

            var cust = principal.FindFirstValue("customer_id");
            var pc = principal.FindFirstValue("computer_id");
            var ep = principal.FindFirstValue("login_epoch");
            if (cust is null || pc is null
                || !Guid.TryParse(cust, out customerId)
                || !Guid.TryParse(pc, out computerId))
                return false;
            if (!string.IsNullOrEmpty(ep) && int.TryParse(ep, out var e))
                epoch = e;
            else
                epoch = 0; // legacy tokens without claim → fail epoch check unless LoginEpoch is 0
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string IssueCustomerToken(Guid customerId, Guid computerId, int loginEpoch, DateTimeOffset expires)
    {
        var key = _configuration["Jwt:SigningKey"]
                  ?? throw new InvalidOperationException("Jwt:SigningKey missing");
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"] ?? "ShiftClub",
            audience: "ShiftClub.ClientCustomer",
            claims:
            [
                new Claim("customer_id", customerId.ToString()),
                new Claim("computer_id", computerId.ToString()),
                new Claim("login_epoch", loginEpoch.ToString())
            ],
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task EnsureLoginAllowedForBookingAsync(
        Computer computer,
        Customer customer,
        CancellationToken cancellationToken)
    {
        var hold = await FindHoldingBookingAsync(computer, cancellationToken);
        if (hold is null)
            return;

        if (IsBookingOwner(hold, customer))
            return;

        throw new InvalidOperationException(FormatBookingBlockMessage(hold));
    }

    /// <summary>
    /// Бронь держит ПК для клиента: soft-hold (за SoftHoldMinutes до начала), прибыл/активна, Reserved.
    /// </summary>
    private async Task<Booking?> FindHoldingBookingAsync(
        Computer computer,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var holdFrom = now.AddMinutes(BookingService.SoftHoldMinutes);

        return await _db.BookingComputers.AsNoTracking()
            .Where(bc => bc.ComputerId == computer.Id
                         && bc.Booking.EndsAt > now
                         && (bc.Booking.Status == BookingStatus.Pending
                             || bc.Booking.Status == BookingStatus.Confirmed
                             || bc.Booking.Status == BookingStatus.Arrived
                             || bc.Booking.Status == BookingStatus.Active)
                         && (bc.Booking.Status == BookingStatus.Arrived
                             || bc.Booking.Status == BookingStatus.Active
                             || computer.Status == ComputerStatus.Reserved
                             || bc.Booking.StartsAt <= holdFrom))
            .OrderBy(bc => bc.Booking.StartsAt)
            .Select(bc => bc.Booking)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static bool IsBookingOwner(Booking booking, Customer? customer)
    {
        if (customer is null)
            return false;

        if (booking.CustomerId.HasValue && booking.CustomerId.Value == customer.Id)
            return true;

        var bookingDigits = DigitsOnly(booking.ContactPhone);
        var customerDigits = DigitsOnly(customer.Phone);
        if (bookingDigits.Length < 10 || customerDigits.Length < 10)
            return false;

        // Только точное совпадение или последние 10 цифр (KZ без кода страны).
        var b10 = bookingDigits[^Math.Min(10, bookingDigits.Length)..];
        var c10 = customerDigits[^Math.Min(10, customerDigits.Length)..];
        return bookingDigits == customerDigits || b10 == c10;
    }

    private static string DigitsOnly(string? value) =>
        new string((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string FormatBookingBlockMessage(Booking booking)
    {
        var from = booking.StartsAt.ToOffset(BranchTimeZone.DefaultOffset);
        var to = booking.EndsAt.ToOffset(BranchTimeZone.DefaultOffset);
        return $"ПК забронирован ({booking.Number}) {from:HH:mm}–{to:HH:mm}. "
               + "Войти может только клиент брони. Для walk-in отмените бронь на кассе.";
    }
}
