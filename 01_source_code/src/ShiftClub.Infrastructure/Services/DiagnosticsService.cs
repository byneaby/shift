using System.Diagnostics;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using ShiftClub.Application.Abstractions;
using ShiftClub.Application.Diagnostics;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Diagnostics;

namespace ShiftClub.Infrastructure.Services;

/// <summary>
/// Одна страница, по которой видно, здоров ли сервер клуба.
/// Нужна прежде всего для поддержки по телефону: вместо «посмотрите в логах»
/// клуб говорит версию, состояние базы, копий и лицензии.
/// </summary>
public sealed class DiagnosticsService : IDiagnosticsService
{
    private static readonly DateTimeOffset ProcessStartedAt =
        new(Process.GetCurrentProcess().StartTime.ToUniversalTime(), TimeSpan.Zero);

    private readonly ShiftClubDbContext _db;
    private readonly IBrandingService _branding;
    private readonly ILicenseService _license;
    private readonly IDatabaseBackupService _backups;
    private readonly IFiscalRegistrar _fiscal;
    private readonly ErrorLogStore _errors;
    private readonly IHostEnvironment _env;

    public DiagnosticsService(
        ShiftClubDbContext db,
        IBrandingService branding,
        ILicenseService license,
        IDatabaseBackupService backups,
        IFiscalRegistrar fiscal,
        ErrorLogStore errors,
        IHostEnvironment env)
    {
        _db = db;
        _branding = branding;
        _license = license;
        _backups = backups;
        _fiscal = fiscal;
        _errors = errors;
        _env = env;
    }

    public static string Version =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            is { } informational && informational.Length > 0
            ? informational.Split('+')[0]
            : Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "неизвестно";

    public async Task<SystemStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<SystemCheckDto>();

        var branding = await _branding.GetAsync(cancellationToken);
        var license = await _license.GetStatusAsync(cancellationToken);
        var backups = await _backups.GetStatusAsync(cancellationToken);

        var (dbOk, dbValue, dbProblem) = await CheckDatabaseAsync(cancellationToken);
        checks.Add(new SystemCheckDto("database", "База данных", dbOk, dbValue, dbProblem));

        var (migrationsOk, migrationsValue, migrationsProblem) = await CheckMigrationsAsync(cancellationToken);
        checks.Add(new SystemCheckDto("migrations", "Миграции", migrationsOk, migrationsValue, migrationsProblem));

        checks.Add(BuildBackupCheck(backups));
        checks.Add(new SystemCheckDto(
            "license",
            "Лицензия",
            license.CanStartSessions,
            license.Summary,
            license.CanStartSessions ? null : license.Warning));

        checks.Add(await BuildComputersCheckAsync(license.MaxComputers, cancellationToken));
        checks.Add(BuildDiskCheck(backups.Directory));

        var fiscal = await _fiscal.GetStatusAsync(cancellationToken);
        checks.Add(new SystemCheckDto(
            "fiscal",
            "Фискальный регистратор",
            // Не подключённый регистратор — не поломка: в части клубов его просто нет.
            // Поломка — это когда он требуется (Fiscal:RequireSuccess), но не настроен.
            fiscal.Enabled || !fiscal.RequireSuccess,
            fiscal.Provider,
            fiscal.Enabled ? null : fiscal.Warning));

        var timeZone = await ResolveTimeZoneAsync(cancellationToken);
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        var uptime = DateTimeOffset.UtcNow - ProcessStartedAt;

        return new SystemStatusDto(
            Version,
            ProcessStartedAt,
            FormatUptime(uptime),
            branding.ClubName,
            license.ClubName,
            license.State.ToString(),
            _env.EnvironmentName,
            Environment.MachineName,
            DateTimeOffset.UtcNow,
            timeZone.Id,
            localNow.ToString("dd.MM.yyyy HH:mm"),
            checks.All(c => c.Ok),
            checks,
            _errors.CountSince(DateTimeOffset.UtcNow.AddDays(-1)),
            _errors.Snapshot(20));
    }

    public IReadOnlyList<ErrorGroupDto> GetErrors(int limit = 50) => _errors.Snapshot(limit);

    public void ClearErrors() => _errors.Clear();

    private async Task<(bool Ok, string Value, string? Problem)> CheckDatabaseAsync(
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        try
        {
            await _db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
            started.Stop();
            return (true, $"отвечает за {started.ElapsedMilliseconds} мс", null);
        }
        catch (Exception ex)
        {
            return (false, "не отвечает", PiiScrubber.Scrub(ex.GetBaseException().Message));
        }
    }

    private async Task<(bool Ok, string Value, string? Problem)> CheckMigrationsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var pending = (await _db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count == 0)
                return (true, "все применены", null);

            return (false, $"не применено: {pending.Count}", $"Ждут применения: {string.Join(", ", pending)}");
        }
        catch (Exception ex)
        {
            return (false, "не удалось проверить", PiiScrubber.Scrub(ex.GetBaseException().Message));
        }
    }

    private static SystemCheckDto BuildBackupCheck(Shared.Contracts.Backups.BackupStatusDto backups)
    {
        if (!backups.Enabled)
            return new SystemCheckDto("backups", "Копии базы", false, "выключены", "Backup:Enabled=false — копий нет.");

        if (!backups.ToolAvailable)
            return new SystemCheckDto("backups", "Копии базы", false, "pg_dump не найден", backups.Warning);

        // Ежедневная копия: если последней успешной нет больше двух суток,
        // клуб уже рискует потерять базу вместе с диском.
        var stale = backups.LastSuccessAt is null
                    || DateTimeOffset.UtcNow - backups.LastSuccessAt.Value > TimeSpan.FromDays(2);

        var value = backups.LastSuccessAt is { } last
            ? $"последняя {last.ToLocalTime():dd.MM HH:mm}, файлов {backups.Files.Count}"
            : "копий ещё нет";

        return new SystemCheckDto(
            "backups",
            "Копии базы",
            !stale,
            value,
            stale ? "Свежей копии нет. Сделайте копию вручную и проверьте расписание." : backups.LastError);
    }

    private async Task<SystemCheckDto> BuildComputersCheckAsync(int maxComputers, CancellationToken cancellationToken)
    {
        var total = await _db.Computers.AsNoTracking().CountAsync(c => !c.IsDeleted, cancellationToken);
        var online = await _db.Computers.AsNoTracking()
            .CountAsync(
                c => !c.IsDeleted
                     && c.LastSeenAt != null
                     && c.LastSeenAt > DateTimeOffset.UtcNow.AddMinutes(-5),
                cancellationToken);

        var limit = maxComputers > 0 ? $", лимит лицензии {maxComputers}" : "";
        var value = total == 0
            ? "ещё не подключены"
            : $"на связи {online} из {total}{limit}";

        return new SystemCheckDto(
            "computers",
            "Компьютеры",
            total == 0 || online > 0,
            value,
            total > 0 && online == 0 ? "Ни один клиентский ПК не отвечает последние 5 минут." : null);
    }

    private static SystemCheckDto BuildDiskCheck(string backupDirectory)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(backupDirectory));
            if (string.IsNullOrWhiteSpace(root))
                return new SystemCheckDto("disk", "Место на диске", true, "не проверяли", null);

            var drive = new DriveInfo(root);
            var freeGb = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
            var ok = freeGb >= 5;

            return new SystemCheckDto(
                "disk",
                "Место на диске",
                ok,
                $"свободно {freeGb:0.#} ГБ на {drive.Name}",
                ok ? null : "Меньше 5 ГБ — копии базы перестанут создаваться.");
        }
        catch (Exception ex)
        {
            return new SystemCheckDto("disk", "Место на диске", true, "не проверяли", PiiScrubber.Scrub(ex.Message));
        }
    }

    private async Task<TimeZoneInfo> ResolveTimeZoneAsync(CancellationToken cancellationToken)
    {
        var id = await _db.Branches.AsNoTracking()
            .OrderBy(b => b.CreatedAt)
            .Select(b => b.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(id))
            return TimeZoneInfo.Utc;

        return TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz) ? tz : TimeZoneInfo.Utc;
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays} дн {uptime.Hours} ч";
        if (uptime.TotalHours >= 1)
            return $"{(int)uptime.TotalHours} ч {uptime.Minutes} мин";
        return $"{(int)uptime.TotalMinutes} мин";
    }
}
