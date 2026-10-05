using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShiftClub.Application.Abstractions;
using ShiftClub.Application.Time;
using ShiftClub.Infrastructure.Options;
using ShiftClub.Infrastructure.Persistence;

namespace ShiftClub.Infrastructure.BackgroundJobs;

/// <summary>
/// Ежедневная копия базы в заданный час по времени филиала.
/// Час считаем по часовому поясу клуба, а не сервера: клуб ночью работает,
/// и копия в 6 утра по Алматы — это не то же самое, что 6 утра по UTC.
/// </summary>
public sealed class DatabaseBackupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<BackupOptions> _options;
    private readonly ILogger<DatabaseBackupWorker> _logger;

    private DateOnly? _lastRunDate;

    public DatabaseBackupWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<BackupOptions> options,
        ILogger<DatabaseBackupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogWarning("Database backups are disabled (Backup:Enabled=false)");
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(40), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var timeZone = await ResolveTimeZoneAsync(scope.ServiceProvider, stoppingToken);
                var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
                var today = DateOnly.FromDateTime(localNow.DateTime);

                if (_lastRunDate != today && localNow.Hour >= _options.Value.DailyHourLocal)
                {
                    var backups = scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>();

                    // Сервер могли перезапустить несколько раз за день, а состояние в памяти
                    // при этом теряется. Поэтому «уже делали сегодня?» спрашиваем у файлов.
                    if (await HasBackupForAsync(backups, timeZone, today, stoppingToken))
                    {
                        _lastRunDate = today;
                    }
                    else
                    {
                        var result = await backups.RunAsync("daily", stoppingToken);
                        _lastRunDate = today;

                        if (!result.Success)
                            _logger.LogError("Daily backup failed: {Message}", result.Message);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Daily backup check failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }
    }

    private static async Task<bool> HasBackupForAsync(
        IDatabaseBackupService backups,
        TimeZoneInfo timeZone,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var status = await backups.GetStatusAsync(cancellationToken);
        return status.Files.Any(f =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(f.CreatedAt, timeZone).DateTime) == date);
    }

    private static async Task<TimeZoneInfo> ResolveTimeZoneAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        try
        {
            var db = services.GetRequiredService<ShiftClubDbContext>();
            var tzId = await db.Branches.AsNoTracking()
                .OrderBy(b => b.CreatedAt)
                .Select(b => b.TimeZoneId)
                .FirstOrDefaultAsync(cancellationToken);
            return BranchTimeZone.Resolve(tzId);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }
}
