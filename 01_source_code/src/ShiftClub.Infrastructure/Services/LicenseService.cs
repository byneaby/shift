using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShiftClub.Application.Abstractions;
using ShiftClub.Application.Licensing;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Licensing;
using ShiftClub.Shared.Licensing;

namespace ShiftClub.Infrastructure.Services;

/// <summary>
/// Разобранный ключ: срок, лимит, фичи. Число занятых ПК здесь НЕ хранится —
/// его надо читать живьём, иначе лимит можно обойти, зарегистрировав пачку ПК
/// быстрее, чем истечёт кэш.
/// </summary>
public sealed record LicenseSnapshot(LicensePayload? Payload, string? ParseError);

/// <summary>
/// Держит разобранный ключ между запросами. Проверка висит на горячем пути
/// (старт сеанса, регистрация ПК), а разбор подписи — это криптография,
/// поэтому результат переиспользуем.
/// </summary>
public sealed class LicenseCache
{
    private readonly object _gate = new();
    private LicenseSnapshot? _snapshot;
    private DateTimeOffset _loadedAt;

    public TimeSpan Ttl { get; } = TimeSpan.FromMinutes(1);

    public LicenseSnapshot? TryGet()
    {
        lock (_gate)
        {
            if (_snapshot is null || DateTimeOffset.UtcNow - _loadedAt > Ttl)
                return null;
            return _snapshot;
        }
    }

    public void Set(LicenseSnapshot snapshot)
    {
        lock (_gate)
        {
            _snapshot = snapshot;
            _loadedAt = DateTimeOffset.UtcNow;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _snapshot = null;
        }
    }
}

public sealed class LicenseService : ILicenseService
{
    public const string LicenseKeySettingKey = "license.key";

    private readonly ShiftClubDbContext _db;
    private readonly IConfiguration _config;
    private readonly LicenseCache _cache;
    private readonly ILogger<LicenseService> _logger;

    public LicenseService(
        ShiftClubDbContext db,
        IConfiguration config,
        LicenseCache cache,
        ILogger<LicenseService> logger)
    {
        _db = db;
        _config = config;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Если ключа нет и enforce выключен — клуб работает без ограничений (режим «свой клуб» / dev).
    /// Для проданного клуба ставится License:Enforce=true, тогда без ключа работать нельзя.
    /// </summary>
    private bool Enforce => _config.GetValue("License:Enforce", false);

    private string PublicKey => _config["License:PublicKey"] ?? "";

    public async Task<LicenseStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = _cache.TryGet();
        if (snapshot is null)
        {
            snapshot = await LoadSnapshotAsync(cancellationToken);
            _cache.Set(snapshot);
        }

        // Число ПК всегда живое: это то, что лимит и ограничивает.
        var usedComputers = await _db.Computers.AsNoTracking()
            .CountAsync(c => !c.IsDeleted, cancellationToken);

        return BuildStatus(snapshot, usedComputers);
    }

    public async Task<LicenseStatusDto> SetKeyAsync(
        string key,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Вставьте лицензионный ключ.");

        var normalized = key.Trim();
        var parsed = LicenseKeyCodec.Parse(normalized, PublicKey);
        if (!parsed.Ok || parsed.Payload is null)
            throw new InvalidOperationException(parsed.Error ?? "Ключ не прошёл проверку.");

        var state = LicenseKeyCodec.ResolveState(parsed.Payload, DateTimeOffset.UtcNow);
        if (state == LicenseState.Expired)
            throw new InvalidOperationException(
                $"Этот ключ уже просрочен (до {parsed.Payload.ExpiresAt:dd.MM.yyyy}). Запросите актуальный.");

        var setting = await _db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == LicenseKeySettingKey && s.BranchId == null, cancellationToken);

        if (setting is null)
        {
            _db.AppSettings.Add(new AppSetting
            {
                Key = LicenseKeySettingKey,
                Value = normalized,
                Description = "Лицензионный ключ SHIFT (подписан вендором)",
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

        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = updatedBy,
            Action = "license.set",
            EntityType = "License",
            EntityId = parsed.Payload.ClubId.ToString(),
            DetailsJson =
                $"{{\"club\":\"{Escape(parsed.Payload.ClubName)}\",\"expiresAt\":\"{parsed.Payload.ExpiresAt:O}\","
                + $"\"maxComputers\":{parsed.Payload.MaxComputers}}}"
        });

        await _db.SaveChangesAsync(cancellationToken);
        InvalidateCache();

        _logger.LogInformation(
            "License installed: club={Club} expires={Expires} maxPc={MaxPc}",
            parsed.Payload.ClubName,
            parsed.Payload.ExpiresAt,
            parsed.Payload.MaxComputers);

        return await GetStatusAsync(cancellationToken);
    }

    public async Task RemoveKeyAsync(Guid? updatedBy, CancellationToken cancellationToken = default)
    {
        var setting = await _db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == LicenseKeySettingKey && s.BranchId == null, cancellationToken);
        if (setting is null)
            return;

        _db.AppSettings.Remove(setting);
        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = updatedBy,
            Action = "license.remove",
            EntityType = "License",
            EntityId = "-"
        });
        await _db.SaveChangesAsync(cancellationToken);
        InvalidateCache();
    }

    public async Task EnsureCanStartSessionAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        if (!status.CanStartSessions)
            throw new InvalidOperationException(LicensePolicy.BlockMessage(status.State, "Запуск сеанса"));
    }

    public async Task EnsureCanRegisterComputerAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        if (!status.CanRegisterComputers)
            throw new InvalidOperationException(LicensePolicy.BlockMessage(status.State, "Регистрация ПК"));

        if (status.HasComputerLimit && status.UsedComputers >= status.MaxComputers)
            throw new InvalidOperationException(LicensePolicy.ComputerLimitMessage(status.MaxComputers));
    }

    public async Task EnsureCanSellAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        if (!status.CanSell)
            throw new InvalidOperationException(LicensePolicy.BlockMessage(status.State, "Продажа"));
    }

    public async Task<bool> HasFeatureAsync(string feature, CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);

        // Без лицензии (свой клуб / dev) доступны все фичи — иначе обновление сломало бы текущий клуб.
        if (status.State == LicenseState.Missing && !Enforce)
            return true;

        return status.Features.Contains(feature, StringComparer.OrdinalIgnoreCase);
    }

    public void InvalidateCache() => _cache.Clear();

    private async Task<LicenseSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken)
    {
        var raw = await _db.AppSettings.AsNoTracking()
            .Where(s => s.Key == LicenseKeySettingKey && s.BranchId == null)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(raw))
            return new LicenseSnapshot(null, null);

        var parsed = LicenseKeyCodec.Parse(raw, PublicKey);
        if (!parsed.Ok || parsed.Payload is null)
        {
            _logger.LogWarning("License key rejected: {Error}", parsed.Error);
            return new LicenseSnapshot(null, parsed.Error ?? "Ключ не прошёл проверку");
        }

        return new LicenseSnapshot(parsed.Payload, null);
    }

    private LicenseStatusDto BuildStatus(LicenseSnapshot snapshot, int usedComputers)
    {
        if (snapshot.ParseError is { } parseError)
        {
            return new LicenseStatusDto(
                LicenseState.Invalid,
                null, null, null, null,
                0,
                usedComputers,
                [],
                CanStartSessions: LicensePolicy.CanStartSessions(LicenseState.Invalid),
                CanRegisterComputers: LicensePolicy.CanRegisterComputers(LicenseState.Invalid),
                CanSell: LicensePolicy.CanSell(LicenseState.Invalid),
                Summary: LicensePolicy.Summary(LicenseState.Invalid, null, null),
                Warning: parseError);
        }

        if (snapshot.Payload is null)
            return MissingStatus(usedComputers);

        var payload = snapshot.Payload;
        var now = DateTimeOffset.UtcNow;
        var state = LicenseKeyCodec.ResolveState(payload, now);
        var daysLeft = (int)Math.Ceiling((payload.ExpiresAt - now).TotalDays);
        var graceEnd = payload.ExpiresAt.AddDays(Math.Max(0, payload.GraceDays));
        var graceDaysLeft = Math.Max(0, (int)Math.Ceiling((graceEnd - now).TotalDays));

        return new LicenseStatusDto(
            state,
            payload.ClubName,
            payload.Plan,
            payload.ExpiresAt,
            daysLeft,
            payload.MaxComputers,
            usedComputers,
            payload.Features,
            CanStartSessions: LicensePolicy.CanStartSessions(state),
            CanRegisterComputers: LicensePolicy.CanRegisterComputers(state),
            CanSell: LicensePolicy.CanSell(state),
            Summary: LicensePolicy.Summary(state, daysLeft, payload.ClubName),
            Warning: LicensePolicy.Warning(state, daysLeft, graceDaysLeft));
    }

    private LicenseStatusDto MissingStatus(int usedComputers)
    {
        // Enforce=true + нет ключа → работать нельзя (клуб не оплатил / не активирован).
        var state = LicenseState.Missing;
        var blocked = Enforce;

        return new LicenseStatusDto(
            state,
            null,
            Enforce ? null : "без лицензии",
            null,
            null,
            MaxComputers: 0,
            UsedComputers: usedComputers,
            Features: Enforce ? [] : LicenseFeatures.All,
            CanStartSessions: !blocked,
            CanRegisterComputers: !blocked,
            CanSell: !blocked,
            Summary: blocked
                ? "Лицензия не установлена — работа заблокирована"
                : "Лицензия не установлена (режим без ограничений)",
            Warning: blocked
                ? "Ключ лицензии не установлен. Вставьте ключ в разделе «Лицензия»."
                : "Лицензия не установлена. Ограничений нет, но для продажи клубу ключ обязателен.");
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
