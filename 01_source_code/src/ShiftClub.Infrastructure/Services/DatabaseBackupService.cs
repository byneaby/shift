using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using ShiftClub.Application.Abstractions;
using ShiftClub.Shared.Contracts.Backups;

namespace ShiftClub.Infrastructure.Services;

/// <summary>
/// Копии базы через pg_dump. Храним результат последней попытки в памяти,
/// чтобы панель показывала «когда последний раз получилось» без лазанья в логи.
/// </summary>
public sealed class BackupRunState
{
    private readonly object _gate = new();

    public DateTimeOffset? LastSuccessAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public string? LastError { get; private set; }

    public void RecordSuccess()
    {
        lock (_gate)
        {
            LastAttemptAt = DateTimeOffset.UtcNow;
            LastSuccessAt = LastAttemptAt;
            LastError = null;
        }
    }

    public void RecordFailure(string error)
    {
        lock (_gate)
        {
            LastAttemptAt = DateTimeOffset.UtcNow;
            LastError = error;
        }
    }
}

public sealed class DatabaseBackupService : IDatabaseBackupService
{
    private const string FilePrefix = "shiftclub";

    private static readonly string[] WindowsPostgresRoots =
    [
        @"C:\Program Files\PostgreSQL",
        @"C:\Program Files (x86)\PostgreSQL"
    ];

    private readonly BackupOptionsSnapshot _options;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly BackupRunState _state;
    private readonly ILogger<DatabaseBackupService> _logger;

    public DatabaseBackupService(
        IOptions<Options.BackupOptions> options,
        IConfiguration config,
        IHostEnvironment env,
        BackupRunState state,
        ILogger<DatabaseBackupService> logger)
    {
        _options = new BackupOptionsSnapshot(options.Value);
        _config = config;
        _env = env;
        _state = state;
        _logger = logger;
    }

    public Task<BackupStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var dir = ResolveDirectory();
        var tool = ResolvePgDump();
        var files = ListFiles(dir);
        var total = files.Sum(f => f.SizeBytes);

        string? warning = null;
        if (!_options.Enabled)
        {
            warning = "Автоматические копии выключены. Для проданного клуба это обязательно включить.";
        }
        else if (tool is null)
        {
            warning = "Не найден pg_dump. Укажите путь в настройке Backup:PgDumpPath — без него копии не создаются.";
        }
        else if (_state.LastError is { } err)
        {
            warning = $"Последняя попытка не удалась: {err}";
        }
        else if (_state.LastSuccessAt is null && files.Count == 0)
        {
            warning = "Копий ещё нет. Нажмите «Сделать копию сейчас», чтобы проверить настройку.";
        }
        else if (files.Count > 0 && DateTimeOffset.UtcNow - files[0].CreatedAt > TimeSpan.FromHours(48))
        {
            warning = "Последняя копия старше двух суток. Проверьте, работает ли расписание.";
        }

        var summary = files.Count == 0
            ? "Копий нет"
            : $"{files.Count} копий, последняя {files[0].CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm}";

        return Task.FromResult(new BackupStatusDto(
            _options.Enabled,
            tool is not null,
            tool,
            dir,
            _options.KeepDays,
            _options.DailyHourLocal,
            _state.LastSuccessAt,
            _state.LastAttemptAt,
            _state.LastError,
            total,
            files,
            summary,
            warning));
    }

    public async Task<RunBackupResultDto> RunAsync(string kind, CancellationToken cancellationToken = default)
    {
        var tool = ResolvePgDump();
        if (tool is null)
        {
            const string message = "Не найден pg_dump. Укажите Backup:PgDumpPath.";
            _state.RecordFailure(message);
            _logger.LogError("Backup skipped: pg_dump not found");
            return new RunBackupResultDto(false, null, 0, message);
        }

        var connectionString = _config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            const string message = "Не настроена строка подключения к базе.";
            _state.RecordFailure(message);
            return new RunBackupResultDto(false, null, 0, message);
        }

        NpgsqlConnectionStringBuilder csb;
        try
        {
            csb = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex)
        {
            var message = $"Строка подключения не разбирается: {ex.Message}";
            _state.RecordFailure(message);
            return new RunBackupResultDto(false, null, 0, message);
        }

        var dir = ResolveDirectory();
        Directory.CreateDirectory(dir);

        var safeKind = SanitizeKind(kind);
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var fileName = $"{FilePrefix}-{stamp}-{safeKind}.dump";
        var fullPath = Path.Combine(dir, fileName);

        var psi = new ProcessStartInfo
        {
            FileName = tool,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("--format=custom");
        psi.ArgumentList.Add("--no-owner");
        psi.ArgumentList.Add("--no-privileges");
        psi.ArgumentList.Add($"--file={fullPath}");
        psi.ArgumentList.Add($"--host={csb.Host ?? "localhost"}");
        psi.ArgumentList.Add($"--port={(csb.Port == 0 ? 5432 : csb.Port)}");
        psi.ArgumentList.Add($"--username={csb.Username}");
        psi.ArgumentList.Add($"--dbname={csb.Database}");

        // Пароль только через переменную окружения — иначе он попадёт в список процессов.
        if (!string.IsNullOrEmpty(csb.Password))
            psi.Environment["PGPASSWORD"] = csb.Password;

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Не удалось запустить pg_dump");

            var stderr = new StringBuilder();
            var errorTask = Task.Run(async () =>
            {
                stderr.Append(await process.StandardError.ReadToEndAsync(cancellationToken));
            }, cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, _options.TimeoutMinutes)));

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                TryDelete(fullPath);
                const string message = "pg_dump не успел за отведённое время.";
                _state.RecordFailure(message);
                return new RunBackupResultDto(false, null, 0, message);
            }

            await errorTask;

            if (process.ExitCode != 0)
            {
                TryDelete(fullPath);
                var message = $"pg_dump завершился с кодом {process.ExitCode}: {Trim(stderr.ToString())}";
                _state.RecordFailure(message);
                _logger.LogError("Backup failed: {Error}", message);
                return new RunBackupResultDto(false, null, 0, message);
            }

            var info = new FileInfo(fullPath);
            if (!info.Exists || info.Length == 0)
            {
                TryDelete(fullPath);
                const string message = "pg_dump отработал, но файл пустой.";
                _state.RecordFailure(message);
                return new RunBackupResultDto(false, null, 0, message);
            }

            _state.RecordSuccess();
            _logger.LogInformation(
                "Backup created: {File} ({SizeMb:0.0} MB)",
                fileName,
                info.Length / 1024.0 / 1024.0);

            // Чистим старое только после успешной копии — иначе при сбое останемся без копий совсем.
            Prune(dir);

            return new RunBackupResultDto(true, fileName, info.Length, $"Копия создана: {fileName}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TryDelete(fullPath);
            var message = $"Ошибка копирования: {ex.Message}";
            _state.RecordFailure(message);
            _logger.LogError(ex, "Backup failed");
            return new RunBackupResultDto(false, null, 0, message);
        }
    }

    public string? ResolveFilePath(string fileName)
    {
        // Никаких путей от клиента: берём только имя файла из нашего каталога.
        var safe = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safe) || !safe.StartsWith(FilePrefix, StringComparison.Ordinal))
            return null;

        var path = Path.Combine(ResolveDirectory(), safe);
        return File.Exists(path) ? path : null;
    }

    private string ResolveDirectory() =>
        Path.IsPathRooted(_options.Directory)
            ? _options.Directory
            : Path.Combine(_env.ContentRootPath, _options.Directory);

    private List<BackupFileDto> ListFiles(string dir)
    {
        if (!Directory.Exists(dir))
            return [];

        return Directory.EnumerateFiles(dir, $"{FilePrefix}-*.dump")
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new BackupFileDto(
                f.Name,
                f.Length,
                new DateTimeOffset(f.CreationTimeUtc, TimeSpan.Zero),
                KindFromName(f.Name)))
            .ToList();
    }

    private void Prune(string dir)
    {
        if (_options.KeepDays <= 0)
            return;

        var cutoff = DateTime.UtcNow.AddDays(-_options.KeepDays);
        var all = ListFiles(dir);

        // Самую свежую копию не удаляем никогда, даже если KeepDays выставили в 1 день.
        foreach (var file in all.Skip(1))
        {
            if (file.CreatedAt.UtcDateTime >= cutoff)
                continue;

            var path = Path.Combine(dir, file.FileName);
            try
            {
                File.Delete(path);
                _logger.LogInformation("Backup pruned: {File}", file.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not prune backup {File}", file.FileName);
            }
        }
    }

    private string? ResolvePgDump()
    {
        if (!string.IsNullOrWhiteSpace(_options.PgDumpPath))
            return File.Exists(_options.PgDumpPath) ? _options.PgDumpPath : null;

        var exe = OperatingSystem.IsWindows() ? "pg_dump.exe" : "pg_dump";

        var fromPath = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(p =>
            {
                try { return Path.Combine(p.Trim(), exe); }
                catch { return null; }
            })
            .FirstOrDefault(p => p is not null && File.Exists(p));

        if (fromPath is not null)
            return fromPath;

        if (!OperatingSystem.IsWindows())
            return null;

        // Типовая установка PostgreSQL на Windows: ...\PostgreSQL\<версия>\bin\pg_dump.exe
        foreach (var root in WindowsPostgresRoots)
        {
            if (!Directory.Exists(root))
                continue;

            var found = Directory.EnumerateDirectories(root)
                .Select(versionDir => Path.Combine(versionDir, "bin", exe))
                .Where(File.Exists)
                .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (found is not null)
                return found;
        }

        return null;
    }

    private static string SanitizeKind(string kind)
    {
        var cleaned = new string((kind ?? "manual")
            .Where(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            .ToArray());
        return cleaned.Length == 0 ? "manual" : cleaned.ToLowerInvariant();
    }

    private static string KindFromName(string fileName)
    {
        var withoutExt = Path.GetFileNameWithoutExtension(fileName);
        var idx = withoutExt.LastIndexOf('-');
        return idx > 0 && idx < withoutExt.Length - 1 ? withoutExt[(idx + 1)..] : "manual";
    }

    private static string Trim(string text) =>
        text.Length <= 400 ? text.Trim() : text.Trim()[..400];

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    private sealed class BackupOptionsSnapshot
    {
        public BackupOptionsSnapshot(Options.BackupOptions o)
        {
            Enabled = o.Enabled;
            Directory = string.IsNullOrWhiteSpace(o.Directory) ? "data/backups" : o.Directory;
            KeepDays = o.KeepDays;
            DailyHourLocal = Math.Clamp(o.DailyHourLocal, 0, 23);
            PgDumpPath = o.PgDumpPath;
            TimeoutMinutes = o.TimeoutMinutes;
        }

        public bool Enabled { get; }
        public string Directory { get; }
        public int KeepDays { get; }
        public int DailyHourLocal { get; }
        public string? PgDumpPath { get; }
        public int TimeoutMinutes { get; }
    }
}
