using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.ClientUpdates;

namespace ShiftClub.Client.Shell;

public sealed class ClientUpdateCoordinator
{
    public static string UpdatesDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ShiftClub",
        "Client",
        "updates");

    public static string UpdatingLockPath => Path.Combine(UpdatesDir, "updating.lock");

    private readonly HttpClient _http;
    private readonly Func<string?> _deviceToken;
    private readonly Func<bool> _isInSession;
    private readonly Action<string> _status;
    private readonly Action? _prepareExit;
    private int _busy;

    public ClientUpdateCoordinator(
        HttpClient http,
        Func<string?> deviceToken,
        Func<bool> isInSession,
        Action<string> status,
        Action? prepareExit = null)
    {
        _http = http;
        _deviceToken = deviceToken;
        _isInSession = isInSession;
        _status = status;
        _prepareExit = prepareExit;
    }

    public async Task CheckAndApplyAsync(bool force, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return;

        try
        {
            var token = _deviceToken();
            if (string.IsNullOrWhiteSpace(token))
                return;

            if (!force && _isInSession())
                return;

            if (force && _isInSession())
            {
                _status("Обновление отложено: активен сеанс");
                return;
            }

            using var checkReq = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/client/updates/check?currentVersion={Uri.EscapeDataString(ClientVersionInfo.Version)}");
            checkReq.Headers.TryAddWithoutValidation("X-Device-Token", token);

            using var checkResp = await _http.SendAsync(checkReq, ct);
            if (!checkResp.IsSuccessStatusCode)
                return;

            var payload = await checkResp.Content.ReadFromJsonAsync<ApiResponse<ClientUpdateCheckResponse>>(ShellJson.Options, cancellationToken: ct);
            var check = payload?.Data;
            if (check is not { UpdateAvailable: true } || string.IsNullOrWhiteSpace(check.DownloadPath))
            {
                if (force)
                    throw new InvalidOperationException("На сервере нет новой версии клиента");
                return;
            }

            _status($"Скачивание обновления {check.LatestVersion}…");
            await ApplyPackageAsync(token, check, ct);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private async Task ApplyPackageAsync(string token, ClientUpdateCheckResponse check, CancellationToken ct)
    {
        Directory.CreateDirectory(UpdatesDir);

        var packagePath = Path.Combine(UpdatesDir, $"pending-{check.LatestVersion}.zip");
        using (var dlReq = new HttpRequestMessage(HttpMethod.Get, check.DownloadPath))
        {
            dlReq.Headers.TryAddWithoutValidation("X-Device-Token", token);
            using var dlResp = await _http.SendAsync(dlReq, HttpCompletionOption.ResponseHeadersRead, ct);
            dlResp.EnsureSuccessStatusCode();
            await using var fs = File.Create(packagePath);
            await dlResp.Content.CopyToAsync(fs, ct);
        }

        if (!string.IsNullOrWhiteSpace(check.Sha256))
        {
            await using var stream = File.OpenRead(packagePath);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
            if (!string.Equals(hash, check.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Хеш пакета не совпал");
        }

        var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var runDir = Path.Combine(UpdatesDir, "runner");
        Directory.CreateDirectory(runDir);

        // Pull only Updater.exe from zip (full extract of ~160MB package OOMs / fills diskless write-cache).
        var updaterExe = ExtractUpdaterExeFromZip(packagePath, runDir);
        if (!File.Exists(updaterExe))
            throw new FileNotFoundException(
                "В пакете обновления нет Updater. Перепубликуйте клиент (Publish-ClientUpdate.ps1).",
                updaterExe);

        var jobPath = Path.Combine(UpdatesDir, "job.json");
        var job = new
        {
            packagePath,
            targetDir = installDir,
            launchExe = Path.Combine(installDir, "ShiftClub.Client.Shell.exe"),
            waitPid = Environment.ProcessId,
            expectedSha256 = check.Sha256
        };
        await File.WriteAllTextAsync(jobPath, JsonSerializer.Serialize(job), ct);

        // Pause Client.Service ACL + Keeper restart while binaries are replaced.
        WriteUpdatingLock();

        // Soften DACL so Updater (same interactive user) can WaitForExit / Kill if Exit is slow.
        try { _prepareExit?.Invoke(); } catch { /* ignore */ }

        _status($"Установка {check.LatestVersion}… перезапуск");

        Process? updaterProc;
        try
        {
            updaterProc = Process.Start(new ProcessStartInfo
            {
                FileName = updaterExe,
                Arguments = $"--job \"{jobPath}\"",
                WorkingDirectory = runDir,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            ClearUpdatingLock();
            throw new InvalidOperationException($"Не удалось запустить Updater: {ex.Message}", ex);
        }

        if (updaterProc is null)
        {
            ClearUpdatingLock();
            throw new InvalidOperationException("Updater не стартовал");
        }

        try
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                try { System.Windows.Application.Current.Shutdown(); } catch { /* ignore */ }
            });
        }
        catch { /* ignore */ }

        // Guaranteed exit so Updater can overwrite binaries (bypasses Closing cancel).
        Environment.Exit(0);
    }

    public static void WriteUpdatingLock()
    {
        try
        {
            Directory.CreateDirectory(UpdatesDir);
            File.WriteAllText(
                UpdatingLockPath,
                $"{DateTimeOffset.UtcNow:O}|pid={Environment.ProcessId}");
        }
        catch
        {
            /* best-effort */
        }
    }

    public static void ClearUpdatingLock()
    {
        try
        {
            if (File.Exists(UpdatingLockPath))
                File.Delete(UpdatingLockPath);
        }
        catch
        {
            /* ignore */
        }
    }

    /// <summary>Extract only the single-file Updater.exe — never unpack the whole Shell package.</summary>
    private static string ExtractUpdaterExeFromZip(string packagePath, string runDir)
    {
        using var zip = ZipFile.OpenRead(packagePath);
        var entry = zip.Entries.FirstOrDefault(e =>
            e.Name.Equals("ShiftClub.Client.Updater.exe", StringComparison.OrdinalIgnoreCase)
            && !e.FullName.Contains("service/", StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException("Updater отсутствует в zip-пакете", packagePath);

        // Unique name avoids locking against a previous updater still mapped in runner/.
        var dest = Path.Combine(runDir, $"ShiftClub.Client.Updater.{Environment.ProcessId}.exe");
        using (var src = entry.Open())
        using (var dst = File.Create(dest))
            src.CopyTo(dst);

        return dest;
    }
}
