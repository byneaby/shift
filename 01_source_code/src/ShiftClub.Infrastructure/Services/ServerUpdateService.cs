using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShiftClub.Application.Abstractions;
using ShiftClub.Application.Versioning;
using ShiftClub.Infrastructure.Options;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.ServerUpdates;

namespace ShiftClub.Infrastructure.Services;

/// <summary>
/// Обновление сервера клуба из канала поставщика.
///
/// Сервер не может переписать собственные файлы, пока работает, поэтому здесь
/// только три шага: спросить у канала, что вышло; скачать пакет и сверить хеш;
/// запустить внешний скрипт подмены. Дальше сервер будет остановлен, а отчёт о
/// результате скрипт оставит файлом — его и читает панель после перезапуска.
/// </summary>
public sealed class ServerUpdateService : IServerUpdateService
{
    public const string HttpClientName = "server-updates";

    private const string RunFileName = "last-run.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>Кэш общий на процесс: канал опрашивать на каждое открытие страницы незачем.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static ServerReleaseManifest? _cachedManifest;
    private static DateTimeOffset _cachedAt;
    private static string? _cachedProblem;

    private readonly ServerUpdateOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILicenseService _license;
    private readonly IDatabaseBackupService _backups;
    private readonly ShiftClubDbContext _db;
    private readonly IHostEnvironment _env;
    private readonly ILogger<ServerUpdateService> _logger;

    public ServerUpdateService(
        IOptions<ServerUpdateOptions> options,
        IHttpClientFactory httpClientFactory,
        ILicenseService license,
        IDatabaseBackupService backups,
        ShiftClubDbContext db,
        IHostEnvironment env,
        ILogger<ServerUpdateService> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _license = license;
        _backups = backups;
        _db = db;
        _env = env;
        _logger = logger;
    }

    public Task<ServerUpdateStatusDto> GetStatusAsync(CancellationToken cancellationToken = default) =>
        BuildStatusAsync(forceRefresh: false, cancellationToken);

    public Task<ServerUpdateStatusDto> CheckAsync(CancellationToken cancellationToken = default) =>
        BuildStatusAsync(forceRefresh: true, cancellationToken);

    public async Task<ServerUpdateStartResultDto> StartAsync(
        Guid employeeId,
        string? version,
        CancellationToken cancellationToken = default)
    {
        var updater = ResolvePath(_options.UpdaterPath);
        if (string.IsNullOrWhiteSpace(_options.UpdaterPath) || !File.Exists(updater))
        {
            return new ServerUpdateStartResultDto(false, null,
                "Скрипт обновления не настроен. Укажите ServerUpdates:UpdaterPath и положите Update-ShiftClubServer.ps1 рядом с сервером.");
        }

        var manifest = await LoadManifestAsync(forceRefresh: true, cancellationToken);
        if (manifest is null)
            return new ServerUpdateStartResultDto(false, null, _cachedProblem ?? "Канал обновлений не ответил.");

        var latest = SemVer.Normalize(manifest.Version) ?? manifest.Version;
        if (version is { Length: > 0 } asked && SemVer.Normalize(asked) != latest)
        {
            return new ServerUpdateStartResultDto(false, null,
                $"В канале сейчас версия {latest}. Обновитесь на неё или проверьте канал заново.");
        }

        if (!SemVer.IsNewer(latest, DiagnosticsService.Version))
            return new ServerUpdateStartResultDto(false, latest, "Установлена актуальная версия.");

        if (BlockedReason(manifest, latest) is { } blocked)
            return new ServerUpdateStartResultDto(false, latest, blocked);

        // Копия базы — до подмены файлов и пока сервер жив: новая версия может
        // накатить миграции, и откат файлов сам по себе их не отменит.
        var backup = await _backups.RunAsync("pre-update", cancellationToken);
        if (!backup.Success)
        {
            return new ServerUpdateStartResultDto(false, latest,
                $"Не удалось сделать копию базы перед обновлением: {backup.Message}. Обновление отменено.");
        }

        string packagePath;
        try
        {
            packagePath = await DownloadAsync(manifest, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogError(ex, "Server update {Version} download failed", latest);
            return new ServerUpdateStartResultDto(false, latest, $"Пакет не скачался: {ex.Message}");
        }

        // Отчёт пишем до запуска скрипта: если сервер упадёт на подмене, в панели
        // после перезапуска будет видно, на чём всё остановилось.
        await WriteRunAsync(new ServerUpdateRunDto(latest, "swapping", "Запущена подмена файлов.", DateTimeOffset.UtcNow, null), cancellationToken);

        _db.AuditLogs.Add(new Domain.Entities.AuditLog
        {
            EmployeeId = employeeId == Guid.Empty ? null : employeeId,
            Action = "server.update.started",
            EntityType = "ServerUpdate",
            EntityId = latest,
            DetailsJson = JsonSerializer.Serialize(new { version = latest, from = DiagnosticsService.Version })
        });
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            LaunchUpdater(updater, packagePath, latest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Server update {Version} updater launch failed", latest);
            await WriteRunAsync(
                new ServerUpdateRunDto(latest, "failed", $"Скрипт обновления не запустился: {ex.Message}", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                cancellationToken);
            return new ServerUpdateStartResultDto(false, latest, $"Скрипт обновления не запустился: {ex.Message}");
        }

        _logger.LogWarning("Server update to {Version} started, server will restart", latest);
        return new ServerUpdateStartResultDto(true, latest,
            $"Обновление до {latest} запущено. Сервер перезапустится; панель будет недоступна несколько минут.");
    }

    private async Task<ServerUpdateStatusDto> BuildStatusAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var current = DiagnosticsService.Version;
        var lastRun = await ReadRunAsync(cancellationToken);
        var ready = FindReadyVersion();

        if (string.IsNullOrWhiteSpace(_options.FeedUrl))
        {
            return new ServerUpdateStatusDto(
                false, current, null, false, null, null, 0,
                "Канал обновлений не указан — сервер обновляется вручную.",
                ready, lastRun);
        }

        var manifest = await LoadManifestAsync(forceRefresh, cancellationToken);
        if (manifest is null)
            return new ServerUpdateStatusDto(true, current, null, false, null, null, 0, _cachedProblem, ready, lastRun);

        var latest = SemVer.Normalize(manifest.Version) ?? manifest.Version;
        var newer = SemVer.IsNewer(latest, current);
        var problem = newer ? BlockedReason(manifest, latest) : null;

        return new ServerUpdateStatusDto(
            true,
            current,
            latest,
            newer && problem is null,
            manifest.ReleaseNotes,
            manifest.PublishedAt,
            manifest.SizeBytes,
            problem,
            ready,
            lastRun);
    }

    private string? BlockedReason(ServerReleaseManifest manifest, string latest)
    {
        if (SemVer.Normalize(manifest.MinVersion) is { } min && SemVer.IsNewer(min, DiagnosticsService.Version))
        {
            return $"Выпуск {latest} ставится с версии {min} и новее, а здесь {DiagnosticsService.Version}. "
                   + "Нужен промежуточный выпуск — напишите поставщику.";
        }

        if (string.IsNullOrWhiteSpace(_options.UpdaterPath) || !File.Exists(ResolvePath(_options.UpdaterPath)))
            return "Скрипт обновления не настроен — обновление из панели недоступно.";

        return null;
    }

    private async Task<ServerReleaseManifest?> LoadManifestAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.FeedUrl))
            return null;

        var fresh = DateTimeOffset.UtcNow - _cachedAt < TimeSpan.FromMinutes(Math.Max(1, _options.CheckIntervalMinutes));
        if (!forceRefresh && fresh && (_cachedManifest is not null || _cachedProblem is not null))
            return _cachedManifest;

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var url = $"{_options.FeedUrl.TrimEnd('/')}/server/{Uri.EscapeDataString(_options.Channel)}/manifest.json";
            var client = _httpClientFactory.CreateClient(HttpClientName);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            await AddClubHeadersAsync(request, cancellationToken);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _cachedManifest = null;
                _cachedProblem = $"Канал обновлений ответил {(int)response.StatusCode}.";
                _cachedAt = DateTimeOffset.UtcNow;
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var manifest = await JsonSerializer.DeserializeAsync<ServerReleaseManifest>(stream, JsonOptions, cancellationToken);

            _cachedManifest = manifest;
            _cachedProblem = manifest is null ? "Канал обновлений вернул непонятный ответ." : null;
            _cachedAt = DateTimeOffset.UtcNow;
            return manifest;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Недоступный канал — не поломка клуба: пишем предупреждением, чтобы
            // это не попало в список ошибок на странице «Состояние».
            _logger.LogWarning("Server update feed unavailable: {Message}", ex.Message);
            _cachedManifest = null;
            _cachedProblem = "Канал обновлений недоступен.";
            _cachedAt = DateTimeOffset.UtcNow;
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Поставщику нужно знать, какой клуб и с какой версии спрашивает.</summary>
    private async Task AddClubHeadersAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation("X-ShiftClub-Version", DiagnosticsService.Version);

        try
        {
            var license = await _license.GetStatusAsync(cancellationToken);
            if (license.ClubName is { Length: > 0 } club)
                request.Headers.TryAddWithoutValidation("X-ShiftClub-Club", club);
            request.Headers.TryAddWithoutValidation("X-ShiftClub-License", license.State.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not read license for update feed headers: {Message}", ex.Message);
        }
    }

    private async Task<string> DownloadAsync(ServerReleaseManifest manifest, CancellationToken cancellationToken)
    {
        var dir = ResolvePath(_options.DownloadPath);
        Directory.CreateDirectory(dir);

        var fileName = Path.GetFileName(manifest.PackageFile);
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("В манифесте указан непонятный файл пакета.");

        var target = Path.Combine(dir, fileName);

        // Повторный запуск после обрыва: если уже скачали и хеш сходится, качать заново незачем.
        if (File.Exists(target) && await HashMatchesAsync(target, manifest.Sha256, cancellationToken))
            return target;

        var url = $"{_options.FeedUrl.TrimEnd('/')}/server/{Uri.EscapeDataString(_options.Channel)}/{Uri.EscapeDataString(fileName)}";
        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        await AddClubHeadersAsync(request, cancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, _options.DownloadTimeoutMinutes)));

        var partial = target + ".part";
        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cts.Token);
            await using var fs = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(fs, cts.Token);
        }

        if (!await HashMatchesAsync(partial, manifest.Sha256, cancellationToken))
        {
            File.Delete(partial);
            throw new InvalidOperationException("Хеш скачанного пакета не сходится с манифестом. Файл удалён.");
        }

        File.Move(partial, target, overwrite: true);
        _logger.LogInformation("Server update package {File} downloaded and verified", fileName);
        return target;
    }

    private void LaunchUpdater(string updaterPath, string packagePath, string version)
    {
        // Отдельный процесс и своё окно: апдейтер остановит этот сервер, и дочерний
        // процесс, привязанный к нему, умер бы вместе с ним.
        var info = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(updaterPath) ?? _env.ContentRootPath
        };

        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-WindowStyle");
        info.ArgumentList.Add("Hidden");
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(updaterPath);
        info.ArgumentList.Add("-PackagePath");
        info.ArgumentList.Add(packagePath);
        info.ArgumentList.Add("-Version");
        info.ArgumentList.Add(version);
        info.ArgumentList.Add("-ReportPath");
        info.ArgumentList.Add(Path.Combine(ResolvePath(_options.DownloadPath), RunFileName));

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("Не удалось запустить powershell.exe");
    }

    private string? FindReadyVersion()
    {
        var dir = ResolvePath(_options.DownloadPath);
        if (!Directory.Exists(dir))
            return null;

        return Directory.EnumerateFiles(dir, "ShiftClub.Server-*.zip")
            .Select(f => SemVer.Normalize(Path.GetFileNameWithoutExtension(f).Replace("ShiftClub.Server-", "")))
            .Where(v => v is not null)
            .OrderByDescending(v => v, Comparer<string?>.Create(SemVer.Compare))
            .FirstOrDefault();
    }

    private async Task<ServerUpdateRunDto?> ReadRunAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(ResolvePath(_options.DownloadPath), RunFileName);
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<ServerUpdateRunDto>(stream, JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning("Could not read server update report: {Message}", ex.Message);
            return null;
        }
    }

    private async Task WriteRunAsync(ServerUpdateRunDto run, CancellationToken cancellationToken)
    {
        var dir = ResolvePath(_options.DownloadPath);
        Directory.CreateDirectory(dir);

        await using var stream = new FileStream(Path.Combine(dir, RunFileName), FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, run, JsonOptions, cancellationToken);
    }

    private static async Task<bool> HashMatchesAsync(string path, string expected, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return false;

        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        return string.Equals(hash, expected.Trim().ToLowerInvariant(), StringComparison.Ordinal);
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(_env.ContentRootPath, path));
    }
}
