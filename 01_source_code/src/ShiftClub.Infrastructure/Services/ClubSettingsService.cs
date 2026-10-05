using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Settings;

namespace ShiftClub.Infrastructure.Services;

public sealed class ClubSettingsService : IClubSettingsService
{
    public const string ShellAdminPasswordKey = "shell.admin_password_hash";
    public const string LoginBackgroundKey = "shell.login_background_url";
    public const string TelegramSettingsKey = "telegram.settings";
    public const string EngagementSettingsKey = "engagement.settings";
    public const string MarketingPromoSettingsKey = "marketing.promo";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly ShiftClubDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IConfiguration _config;
    private readonly ITelegramBotRuntime _telegramRuntime;

    public ClubSettingsService(
        ShiftClubDbContext db,
        IPasswordHasher hasher,
        IConfiguration config,
        ITelegramBotRuntime telegramRuntime)
    {
        _db = db;
        _hasher = hasher;
        _config = config;
        _telegramRuntime = telegramRuntime;
    }

    public async Task<ShellAdminPasswordStatusDto> GetShellAdminStatusAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);
        var has = await _db.AppSettings.AsNoTracking()
            .AnyAsync(s => s.Key == ShellAdminPasswordKey && s.Value != "", cancellationToken);
        return new ShellAdminPasswordStatusDto(has);
    }

    public async Task SetShellAdminPasswordAsync(
        string password,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Trim().Length < 6)
            throw new InvalidOperationException("Пароль админ-режима Shell — минимум 6 символов.");

        var setting = await _db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == ShellAdminPasswordKey && s.BranchId == null, cancellationToken);

        var hash = _hasher.Hash(password.Trim());
        if (setting is null)
        {
            _db.AppSettings.Add(new AppSetting
            {
                Key = ShellAdminPasswordKey,
                Value = hash,
                Description = "Пароль админ-режима Shell (настройка образа / superclient)",
                CreatedBy = updatedBy,
                UpdatedBy = updatedBy,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            setting.Value = hash;
            setting.UpdatedBy = updatedBy;
            setting.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> VerifyShellAdminPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password))
            return false;

        await EnsureSeededAsync(cancellationToken);

        var hash = await _db.AppSettings.AsNoTracking()
            .Where(s => s.Key == ShellAdminPasswordKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(hash))
            return false;

        return _hasher.Verify(password.Trim(), hash);
    }

    public async Task<LoginBackgroundDto> GetLoginBackgroundAsync(CancellationToken cancellationToken = default)
    {
        var url = await _db.AppSettings.AsNoTracking()
            .Where(s => s.Key == LoginBackgroundKey && s.BranchId == null)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
        return new LoginBackgroundDto(string.IsNullOrWhiteSpace(url) ? null : url.Trim());
    }

    public async Task<LoginBackgroundDto> SetLoginBackgroundUrlAsync(
        string url,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("URL фона пустой");

        var normalized = url.Trim();
        var setting = await _db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == LoginBackgroundKey && s.BranchId == null, cancellationToken);

        if (setting is null)
        {
            _db.AppSettings.Add(new AppSetting
            {
                Key = LoginBackgroundKey,
                Value = normalized,
                Description = "Фон экрана входа Shell",
                CreatedBy = updatedBy,
                UpdatedBy = updatedBy,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            setting.Value = normalized;
            setting.UpdatedBy = updatedBy;
            setting.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new LoginBackgroundDto(normalized);
    }

    public async Task ClearLoginBackgroundAsync(Guid? updatedBy, CancellationToken cancellationToken = default)
    {
        var setting = await _db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == LoginBackgroundKey && s.BranchId == null, cancellationToken);
        if (setting is null)
            return;

        _db.AppSettings.Remove(setting);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        var exists = await _db.AppSettings.AnyAsync(s => s.Key == ShellAdminPasswordKey, cancellationToken);
        if (exists)
            return;

        var seed = _config["Seed:OwnerPassword"];
        if (string.IsNullOrWhiteSpace(seed))
            seed = "Owner123!";

        _db.AppSettings.Add(new AppSetting
        {
            Key = ShellAdminPasswordKey,
            Value = _hasher.Hash(seed),
            Description = "Пароль админ-режима Shell (настройка образа / superclient)"
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<TelegramBotStoredSettings> GetTelegramBotStoredAsync(CancellationToken cancellationToken = default)
    {
        var raw = await _db.AppSettings.AsNoTracking()
            .Where(s => s.Key == TelegramSettingsKey && s.BranchId == null)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(raw))
            return new TelegramBotStoredSettings();

        try
        {
            return JsonSerializer.Deserialize<TelegramBotStoredSettings>(raw, JsonOptions)
                   ?? new TelegramBotStoredSettings();
        }
        catch
        {
            return new TelegramBotStoredSettings();
        }
    }

    public async Task<TelegramBotSettingsDto> GetTelegramBotSettingsAsync(CancellationToken cancellationToken = default)
    {
        var stored = await GetTelegramBotStoredAsync(cancellationToken);
        return MapTelegramDto(stored);
    }

    public async Task<TelegramBotSettingsDto> UpdateTelegramBotSettingsAsync(
        UpdateTelegramBotSettingsRequest request,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        var stored = await GetTelegramBotStoredAsync(cancellationToken);

        stored.Enabled = request.Enabled;
        stored.NotifyHelp = request.NotifyHelp;
        stored.NotifySecurity = request.NotifySecurity;
        stored.NotifyBar = request.NotifyBar;
        stored.NotifySessionWarning = request.NotifySessionWarning;
        stored.NotifyBooking = request.NotifyBooking;
        stored.DefaultEmployeeId = request.DefaultEmployeeId;

        if (request.PublicWebAppBaseUrl is not null)
        {
            var url = request.PublicWebAppBaseUrl.Trim().TrimEnd('/');
            if (url.Length == 0)
                stored.PublicWebAppBaseUrl = null;
            else if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Адрес Mini App должен начинаться с https:// (требование Telegram).");
            else
                stored.PublicWebAppBaseUrl = url;
        }

        if (request.AllowedUsers is not null)
        {
            stored.AllowedUsers = request.AllowedUsers
                .Where(u => u.TelegramUserId != 0)
                .GroupBy(u => u.TelegramUserId)
                .Select(g => g.Last())
                .ToList();
        }

        if (request.AlertChatIds is not null)
            stored.AlertChatIds = request.AlertChatIds.Where(id => id != 0).Distinct().ToList();

        if (request.BotToken is not null)
        {
            var token = request.BotToken.Trim();
            if (token.Length == 0)
            {
                stored.BotToken = null;
                stored.BotUsername = null;
            }
            else if (!LooksLikeMaskedToken(token))
            {
                if (!token.Contains(':', StringComparison.Ordinal) || token.Length < 20)
                    throw new InvalidOperationException("Токен бота выглядит некорректно (нужен токен от @BotFather).");
                stored.BotToken = token;
            }
        }

        var json = JsonSerializer.Serialize(stored, JsonOptions);
        var setting = await _db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == TelegramSettingsKey && s.BranchId == null, cancellationToken);

        if (setting is null)
        {
            _db.AppSettings.Add(new AppSetting
            {
                Key = TelegramSettingsKey,
                Value = json,
                Description = "Настройки Telegram-бота staff",
                CreatedBy = updatedBy,
                UpdatedBy = updatedBy,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            setting.Value = json;
            setting.UpdatedBy = updatedBy;
            setting.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        _telegramRuntime.RequestRestart();
        return MapTelegramDto(stored);
    }

    public async Task PersistTelegramBotUsernameAsync(string? username, CancellationToken cancellationToken = default)
    {
        username = username?.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(username))
            return;

        var stored = await GetTelegramBotStoredAsync(cancellationToken);
        if (string.Equals(stored.BotUsername, username, StringComparison.OrdinalIgnoreCase))
            return;

        stored.BotUsername = username;
        var json = JsonSerializer.Serialize(stored, JsonOptions);
        var setting = await _db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == TelegramSettingsKey && s.BranchId == null, cancellationToken);
        if (setting is null)
            return;
        setting.Value = json;
        setting.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<TelegramUsersAdminDto> GetTelegramUsersAdminAsync(CancellationToken cancellationToken = default)
    {
        var stored = await GetTelegramBotStoredAsync(cancellationToken);
        var allow = stored.AllowedUsers.Select(u => u.TelegramUserId).ToHashSet();

        var customers = await _db.Customers.AsNoTracking()
            .Where(c => c.TelegramUserId != null)
            .OrderByDescending(c => c.TelegramLinkedAt)
            .Select(c => new TelegramLinkedCustomerDto(
                c.Id,
                ((c.FirstName ?? "") + " " + (c.LastName ?? "")).Trim(),
                c.Phone,
                c.TelegramUserId!.Value,
                c.TelegramLinkedAt,
                c.IsActive))
            .Take(500)
            .ToListAsync(cancellationToken);

        var employeesRaw = await _db.Employees.AsNoTracking()
            .Where(e => e.TelegramUserId != null || e.IsActive)
            .OrderBy(e => e.DisplayName)
            .Take(500)
            .Select(e => new
            {
                e.Id,
                e.Login,
                e.DisplayName,
                e.Phone,
                e.TelegramUserId,
                e.TelegramLinkedAt,
                e.IsActive
            })
            .ToListAsync(cancellationToken);

        var employees = employeesRaw
            .Where(e => e.TelegramUserId is not null || stored.AllowedUsers.Any(a => a.EmployeeId == e.Id))
            .Select(e => new TelegramLinkedEmployeeDto(
                e.Id,
                e.Login,
                e.DisplayName,
                e.Phone,
                e.TelegramUserId,
                e.TelegramLinkedAt,
                e.TelegramUserId is not null && allow.Contains(e.TelegramUserId.Value),
                e.IsActive))
            .ToList();

        return new TelegramUsersAdminDto(
            customers.Count,
            employees.Count(e => e.TelegramUserId is not null),
            stored.AllowedUsers.Count,
            customers,
            employees);
    }

    public async Task UnlinkCustomerTelegramAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)
                       ?? throw new KeyNotFoundException("Клиент не найден");
        if (customer.TelegramUserId is null)
            return;
        customer.TelegramUserId = null;
        customer.TelegramLinkedAt = null;
        customer.TelegramChangedAt = DateTimeOffset.UtcNow;
        customer.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UnlinkEmployeeTelegramAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken)
                       ?? throw new KeyNotFoundException("Сотрудник не найден");
        var tgId = employee.TelegramUserId;
        employee.TelegramUserId = null;
        employee.TelegramLinkedAt = null;
        employee.UpdatedAt = DateTimeOffset.UtcNow;

        if (tgId is not null)
        {
            var stored = await GetTelegramBotStoredAsync(cancellationToken);
            var before = stored.AllowedUsers.Count;
            stored.AllowedUsers = stored.AllowedUsers
                .Where(u => u.TelegramUserId != tgId.Value && u.EmployeeId != employeeId)
                .ToList();
            if (stored.AllowedUsers.Count != before)
                await SaveTelegramStoredAsync(stored, null, restartBot: true, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MakeStaffFromTelegramAsync(
        TelegramMakeStaffRequest request,
        CancellationToken cancellationToken = default)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == request.EmployeeId && e.IsActive, cancellationToken)
                       ?? throw new InvalidOperationException("Сотрудник не найден");

        long? tgId = request.TelegramUserId;
        string? displayName = null;
        if (request.CustomerId is { } cid)
        {
            var customer = await _db.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == cid, cancellationToken)
                ?? throw new InvalidOperationException("Клиент не найден");
            if (customer.TelegramUserId is null)
                throw new InvalidOperationException("У клиента не привязан Telegram");
            tgId = customer.TelegramUserId;
            displayName = $"{customer.FirstName} {customer.LastName}".Trim();
        }

        if (tgId is null or 0)
            throw new InvalidOperationException("Укажите Telegram ID или клиента с привязанным Telegram");

        var taken = await _db.Employees.AnyAsync(
            e => e.TelegramUserId == tgId && e.Id != employee.Id, cancellationToken);
        if (taken)
            throw new InvalidOperationException("Этот Telegram уже привязан к другому сотруднику");

        employee.TelegramUserId = tgId;
        employee.TelegramLinkedAt = DateTimeOffset.UtcNow;
        employee.UpdatedAt = DateTimeOffset.UtcNow;

        var stored = await GetTelegramBotStoredAsync(cancellationToken);
        stored.AllowedUsers = stored.AllowedUsers
            .Where(u => u.TelegramUserId != tgId.Value)
            .Append(new TelegramAllowedUserDto(tgId.Value, employee.Id, displayName ?? employee.DisplayName, true))
            .ToList();
        await SaveTelegramStoredAsync(stored, null, restartBot: false, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SaveTelegramStoredAsync(
        TelegramBotStoredSettings stored,
        Guid? updatedBy,
        bool restartBot,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(stored, JsonOptions);
        var setting = await _db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == TelegramSettingsKey && s.BranchId == null, cancellationToken);
        if (setting is null)
        {
            _db.AppSettings.Add(new AppSetting
            {
                Key = TelegramSettingsKey,
                Value = json,
                Description = "Настройки Telegram-бота staff",
                CreatedBy = updatedBy,
                UpdatedBy = updatedBy,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            setting.Value = json;
            setting.UpdatedBy = updatedBy;
            setting.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        if (restartBot)
            _telegramRuntime.RequestRestart();
    }

    public async Task<EngagementStoredSettings> GetEngagementStoredAsync(CancellationToken cancellationToken = default)
    {
        var raw = await _db.AppSettings.AsNoTracking()
            .Where(s => s.Key == EngagementSettingsKey && s.BranchId == null)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(raw))
            return new EngagementStoredSettings();

        try
        {
            var s = JsonSerializer.Deserialize<EngagementStoredSettings>(raw, JsonOptions)
                    ?? new EngagementStoredSettings();
            NormalizeEngagement(s);
            return s;
        }
        catch
        {
            return new EngagementStoredSettings();
        }
    }

    public async Task<EngagementSettingsDto> GetEngagementSettingsAsync(CancellationToken cancellationToken = default)
    {
        var s = await GetEngagementStoredAsync(cancellationToken);
        // Prefer telegram-stored webapp URL if engagement one empty (single place in TG settings)
        var tg = await GetTelegramBotStoredAsync(cancellationToken);
        var webApp = !string.IsNullOrWhiteSpace(s.PublicWebAppBaseUrl)
            ? s.PublicWebAppBaseUrl
            : tg.PublicWebAppBaseUrl;
        return MapEngagementDto(s, webApp);
    }

    public async Task<EngagementSettingsDto> UpdateEngagementSettingsAsync(
        UpdateEngagementSettingsRequest request,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        if (request.BirthdayBonusAmount < 0 || request.BirthdayTimeBankMinutes < 0)
            throw new InvalidOperationException("Подарок на день рождения не может быть отрицательным.");
        if (request.TelegramChangeCooldownDays is < 1 or > 365)
            throw new InvalidOperationException("Интервал смены Telegram: от 1 до 365 дней.");

        var tiers = (request.StreakTiers ?? [])
            .Where(t => t.Days > 0)
            .GroupBy(t => t.Days)
            .Select(g => g.Last())
            .OrderBy(t => t.Days)
            .Select(t => new EngagementStreakTier
            {
                Days = t.Days,
                BonusAmount = Math.Max(0, t.BonusAmount),
                BarRewards = Math.Max(0, t.BarRewards)
            })
            .ToList();

        if (tiers.Count == 0)
            throw new InvalidOperationException("Добавьте хотя бы один порог стрика.");

        var depositTiers = (request.DepositBonusTiers ?? [])
            .Where(t => t.MinAmount > 0)
            .GroupBy(t => t.MinAmount)
            .Select(g => g.Last())
            .OrderBy(t => t.MinAmount)
            .Select(t => new EngagementDepositBonusTier
            {
                MinAmount = Math.Round(t.MinAmount, 0, MidpointRounding.AwayFromZero),
                BonusAmount = Math.Max(0, Math.Round(t.BonusAmount, 0, MidpointRounding.AwayFromZero))
            })
            .Where(t => t.BonusAmount > 0)
            .ToList();

        if (request.DepositBonusEnabled && depositTiers.Count == 0)
            throw new InvalidOperationException("Включили бонус за пополнение — добавьте хотя бы один порог (сумма → бонус).");

        string? webApp = null;
        if (request.PublicWebAppBaseUrl is not null)
        {
            var url = request.PublicWebAppBaseUrl.Trim().TrimEnd('/');
            if (url.Length == 0)
                webApp = "";
            else if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Адрес Mini App должен начинаться с https://");
            else
                webApp = url;
        }

        var stored = await GetEngagementStoredAsync(cancellationToken);
        stored.Enabled = request.Enabled;
        stored.StreakTiers = tiers;
        stored.BirthdayBonusAmount = request.BirthdayBonusAmount;
        stored.BirthdayTimeBankMinutes = request.BirthdayTimeBankMinutes;
        stored.TelegramChangeCooldownDays = request.TelegramChangeCooldownDays;
        stored.DepositBonusEnabled = request.DepositBonusEnabled;
        stored.DepositBonusStackWithLoyaltyPercent = request.DepositBonusStackWithLoyaltyPercent;
        stored.DepositBonusTiers = depositTiers;
        if (webApp is not null)
            stored.PublicWebAppBaseUrl = webApp.Length == 0 ? null : webApp;

        // Mirror webapp URL into telegram settings for bot keyboard
        if (webApp is not null)
        {
            var tg = await GetTelegramBotStoredAsync(cancellationToken);
            tg.PublicWebAppBaseUrl = stored.PublicWebAppBaseUrl;
            var tgJson = JsonSerializer.Serialize(tg, JsonOptions);
            var tgSetting = await _db.AppSettings
                .FirstOrDefaultAsync(s => s.Key == TelegramSettingsKey && s.BranchId == null, cancellationToken);
            if (tgSetting is not null)
            {
                tgSetting.Value = tgJson;
                tgSetting.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        var json = JsonSerializer.Serialize(stored, JsonOptions);
        var setting = await _db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == EngagementSettingsKey && s.BranchId == null, cancellationToken);
        if (setting is null)
        {
            _db.AppSettings.Add(new AppSetting
            {
                Key = EngagementSettingsKey,
                Value = json,
                Description = "Награды: стрик, день рождения, смена Telegram",
                CreatedBy = updatedBy,
                UpdatedBy = updatedBy,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            setting.Value = json;
            setting.UpdatedBy = updatedBy;
            setting.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return MapEngagementDto(stored, stored.PublicWebAppBaseUrl);
    }

    private static void NormalizeEngagement(EngagementStoredSettings s)
    {
        if (s.StreakTiers is null || s.StreakTiers.Count == 0)
        {
            s.StreakTiers =
            [
                new() { Days = 3, BonusAmount = 200, BarRewards = 0 },
                new() { Days = 5, BonusAmount = 500, BarRewards = 1 },
                new() { Days = 7, BonusAmount = 1000, BarRewards = 1 }
            ];
        }

        if (s.TelegramChangeCooldownDays <= 0)
            s.TelegramChangeCooldownDays = 30;

        s.DepositBonusTiers ??=
        [
            new() { MinAmount = 3000, BonusAmount = 300 },
            new() { MinAmount = 5000, BonusAmount = 500 },
            new() { MinAmount = 10000, BonusAmount = 1500 }
        ];
    }

    private static EngagementSettingsDto MapEngagementDto(EngagementStoredSettings s, string? webApp) =>
        new(
            s.Enabled,
            s.StreakTiers
                .OrderBy(t => t.Days)
                .Select(t => new EngagementStreakTierDto(t.Days, t.BonusAmount, t.BarRewards))
                .ToList(),
            s.BirthdayBonusAmount,
            s.BirthdayTimeBankMinutes,
            s.TelegramChangeCooldownDays,
            webApp ?? s.PublicWebAppBaseUrl,
            s.DepositBonusEnabled,
            s.DepositBonusStackWithLoyaltyPercent,
            (s.DepositBonusTiers ?? [])
                .OrderBy(t => t.MinAmount)
                .Select(t => new EngagementDepositBonusTierDto(t.MinAmount, t.BonusAmount))
                .ToList());

    public async Task<MarketingPromoStoredSettings> GetMarketingPromoStoredAsync(CancellationToken cancellationToken = default)
    {
        var setting = await _db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == MarketingPromoSettingsKey && s.BranchId == null, cancellationToken);

        if (setting is null || string.IsNullOrWhiteSpace(setting.Value))
        {
            var seeded = DefaultMarketingPromo();
            var json = JsonSerializer.Serialize(seeded, JsonOptions);
            if (setting is null)
            {
                _db.AppSettings.Add(new AppSetting
                {
                    Key = MarketingPromoSettingsKey,
                    Value = json,
                    Description = "Маркетинговая скидка на тарифы (прайс / сеансы)",
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                setting.Value = json;
                setting.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await _db.SaveChangesAsync(cancellationToken);
            return seeded;
        }

        try
        {
            var s = JsonSerializer.Deserialize<MarketingPromoStoredSettings>(setting.Value, JsonOptions)
                    ?? DefaultMarketingPromo();
            NormalizeMarketingPromo(s);
            return s;
        }
        catch
        {
            return DefaultMarketingPromo();
        }
    }

    public async Task<MarketingPromoSettingsDto> GetMarketingPromoSettingsAsync(CancellationToken cancellationToken = default)
    {
        var s = await GetMarketingPromoStoredAsync(cancellationToken);
        return MapMarketingPromoDto(s);
    }

    public async Task<MarketingPromoSettingsDto> UpdateMarketingPromoSettingsAsync(
        UpdateMarketingPromoRequest request,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        var percent = MarketingPromoMath.ClampPercent(request.Percent);
        if (request.Enabled && percent <= 0)
            throw new InvalidOperationException("Укажите процент скидки больше 0.");
        if (request.StartsAt is { } a && request.EndsAt is { } b && a > b)
            throw new InvalidOperationException("Дата начала акции не может быть позже окончания.");

        var label = string.IsNullOrWhiteSpace(request.Label)
            ? $"−{percent:0}% на тарифы"
            : request.Label.Trim();
        if (label.Length > 80)
            label = label[..80];

        var title = string.IsNullOrWhiteSpace(request.Title) ? "Акция" : request.Title.Trim();
        if (title.Length > 40)
            title = title[..40];

        var stored = new MarketingPromoStoredSettings
        {
            Enabled = request.Enabled,
            Percent = percent,
            Label = label,
            Title = title,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            ApplyToTariffs = request.ApplyToTariffs
        };
        NormalizeMarketingPromo(stored);

        var json = JsonSerializer.Serialize(stored, JsonOptions);
        var setting = await _db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == MarketingPromoSettingsKey && s.BranchId == null, cancellationToken);
        if (setting is null)
        {
            _db.AppSettings.Add(new AppSetting
            {
                Key = MarketingPromoSettingsKey,
                Value = json,
                Description = "Маркетинговая скидка на тарифы (прайс / сеансы)",
                CreatedBy = updatedBy,
                UpdatedBy = updatedBy,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            setting.Value = json;
            setting.UpdatedBy = updatedBy;
            setting.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return MapMarketingPromoDto(stored);
    }

    private static MarketingPromoStoredSettings DefaultMarketingPromo()
    {
        // Кампания «1 месяц −50%»: август → сентябрь 2026 (Алматы UTC+5).
        var start = new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.FromHours(5));
        var end = new DateTimeOffset(2026, 9, 6, 23, 59, 59, TimeSpan.FromHours(5));
        return new MarketingPromoStoredSettings
        {
            Enabled = true,
            Percent = 50,
            Label = "1 месяц −50%",
            Title = "Акция",
            StartsAt = start,
            EndsAt = end,
            ApplyToTariffs = true
        };
    }

    private static void NormalizeMarketingPromo(MarketingPromoStoredSettings s)
    {
        s.Percent = MarketingPromoMath.ClampPercent(s.Percent);
        if (string.IsNullOrWhiteSpace(s.Label))
            s.Label = s.Percent > 0 ? $"−{s.Percent:0}%" : "Акция";
        if (string.IsNullOrWhiteSpace(s.Title))
            s.Title = "Акция";
    }

    private static MarketingPromoSettingsDto MapMarketingPromoDto(MarketingPromoStoredSettings s) =>
        new(
            s.Enabled,
            s.Percent,
            s.Label,
            s.Title,
            s.StartsAt,
            s.EndsAt,
            s.ApplyToTariffs,
            MarketingPromoMath.IsActive(s, DateTimeOffset.UtcNow));

    public const string TelegramCrmSettingsKey = "telegram.crm";

    public async Task<TelegramCrmStoredSettings> GetTelegramCrmStoredAsync(CancellationToken cancellationToken = default)
    {
        var setting = await _db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == TelegramCrmSettingsKey && s.BranchId == null, cancellationToken);
        if (setting is null || string.IsNullOrWhiteSpace(setting.Value))
            return DefaultTelegramCrm();

        try
        {
            var s = JsonSerializer.Deserialize<TelegramCrmStoredSettings>(setting.Value, JsonOptions)
                    ?? DefaultTelegramCrm();
            NormalizeTelegramCrm(s);
            return s;
        }
        catch
        {
            return DefaultTelegramCrm();
        }
    }

    public async Task<TelegramCrmSettingsDto> GetTelegramCrmSettingsAsync(CancellationToken cancellationToken = default)
    {
        var s = await GetTelegramCrmStoredAsync(cancellationToken);
        return MapTelegramCrmDto(s, new TelegramCrmStatsDto(0, 0, 0, 0, 0));
    }

    public async Task<TelegramCrmSettingsDto> UpdateTelegramCrmSettingsAsync(
        UpdateTelegramCrmSettingsRequest request,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        var stored = new TelegramCrmStoredSettings
        {
            Enabled = request.Enabled,
            QuietHourFrom = request.QuietHourFrom,
            QuietHourTo = request.QuietHourTo,
            MinDaysBetweenMessages = request.MinDaysBetweenMessages,
            MaxSendsPerTick = request.MaxSendsPerTick,
            MaxSendsPerDay = request.MaxSendsPerDay,
            WinbackEnabled = request.WinbackEnabled,
            WinbackAfterDays = request.WinbackAfterDays,
            WinbackCooldownDays = request.WinbackCooldownDays,
            UnusedKeyEnabled = request.UnusedKeyEnabled,
            UnusedKeyMinIdleDays = request.UnusedKeyMinIdleDays,
            UnusedKeyCooldownDays = request.UnusedKeyCooldownDays,
            PromoNudgeEnabled = request.PromoNudgeEnabled,
            PromoNudgeCooldownDays = request.PromoNudgeCooldownDays
        };
        NormalizeTelegramCrm(stored);

        var json = JsonSerializer.Serialize(stored, JsonOptions);
        var setting = await _db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == TelegramCrmSettingsKey && s.BranchId == null, cancellationToken);
        if (setting is null)
        {
            _db.AppSettings.Add(new AppSetting
            {
                Key = TelegramCrmSettingsKey,
                Value = json,
                Description = "Автосообщения Telegram CRM (win-back / ключ / промо)",
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = updatedBy
            });
        }
        else
        {
            setting.Value = json;
            setting.UpdatedAt = DateTimeOffset.UtcNow;
            setting.UpdatedBy = updatedBy;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return MapTelegramCrmDto(stored, new TelegramCrmStatsDto(0, 0, 0, 0, 0));
    }

    private static TelegramCrmStoredSettings DefaultTelegramCrm() => new();

    private static void NormalizeTelegramCrm(TelegramCrmStoredSettings s)
    {
        s.QuietHourFrom = Math.Clamp(s.QuietHourFrom, 0, 23);
        s.QuietHourTo = Math.Clamp(s.QuietHourTo, 1, 24);
        s.MinDaysBetweenMessages = Math.Clamp(s.MinDaysBetweenMessages, 2, 30);
        s.MaxSendsPerTick = Math.Clamp(s.MaxSendsPerTick, 1, 100);
        s.MaxSendsPerDay = Math.Clamp(s.MaxSendsPerDay, 5, 500);
        s.WinbackAfterDays = Math.Clamp(s.WinbackAfterDays, 5, 60);
        s.WinbackCooldownDays = Math.Clamp(s.WinbackCooldownDays, 7, 90);
        s.UnusedKeyMinIdleDays = Math.Clamp(s.UnusedKeyMinIdleDays, 1, 30);
        s.UnusedKeyCooldownDays = Math.Clamp(s.UnusedKeyCooldownDays, 5, 60);
        s.PromoNudgeCooldownDays = Math.Clamp(s.PromoNudgeCooldownDays, 5, 45);
    }

    private static TelegramCrmSettingsDto MapTelegramCrmDto(TelegramCrmStoredSettings s, TelegramCrmStatsDto stats) =>
        new(
            s.Enabled,
            s.QuietHourFrom,
            s.QuietHourTo,
            s.MinDaysBetweenMessages,
            s.MaxSendsPerTick,
            s.MaxSendsPerDay,
            s.WinbackEnabled,
            s.WinbackAfterDays,
            s.WinbackCooldownDays,
            s.UnusedKeyEnabled,
            s.UnusedKeyMinIdleDays,
            s.UnusedKeyCooldownDays,
            s.PromoNudgeEnabled,
            s.PromoNudgeCooldownDays,
            stats);

    private TelegramBotSettingsDto MapTelegramDto(TelegramBotStoredSettings stored)
    {
        var hasToken = !string.IsNullOrWhiteSpace(stored.BotToken);
        return new TelegramBotSettingsDto(
            stored.Enabled,
            hasToken,
            hasToken ? MaskToken(stored.BotToken!) : null,
            stored.BotUsername ?? _telegramRuntime.BotUsername,
            stored.AllowedUsers,
            stored.AlertChatIds,
            stored.NotifyHelp,
            stored.NotifySecurity,
            stored.NotifyBar,
            stored.NotifySessionWarning,
            stored.NotifyBooking,
            stored.DefaultEmployeeId,
            _telegramRuntime.Status,
            _telegramRuntime.Detail,
            stored.PublicWebAppBaseUrl);
    }

    private static string MaskToken(string token)
    {
        var parts = token.Split(':', 2);
        if (parts.Length != 2 || parts[1].Length < 8)
            return "***";
        return $"{parts[0]}:{new string('*', Math.Max(4, parts[1].Length - 4))}{parts[1][^4..]}";
    }

    private static bool LooksLikeMaskedToken(string token) =>
        token.Contains("****", StringComparison.Ordinal) || token.Contains("***", StringComparison.Ordinal);
}
