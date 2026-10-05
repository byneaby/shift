using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ShiftClub.Shared.Security;

namespace ShiftClub.Client.Updater;

internal static class Program
{
    private static string UpdatesDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ShiftClub",
        "Client",
        "updates");

    private static string UpdatingLockPath => Path.Combine(UpdatesDir, "updating.lock");

    private static int Main(string[] args)
    {
        try
        {
            var opts = UpdateArgs.Parse(args);
            Directory.CreateDirectory(UpdatesDir);
            var logPath = Path.Combine(UpdatesDir, "updater.log");

            void Log(string msg)
            {
                var line = $"{DateTimeOffset.Now:O} {msg}";
                Console.WriteLine(line);
                File.AppendAllText(logPath, line + Environment.NewLine);
            }

            // Keep watchdog paused for the whole update.
            TouchUpdatingLock();
            Log($"Updater start. waitPid={opts.WaitPid} package={opts.PackagePath} target={opts.TargetDir}");

            // Enable SeDebugPrivilege before any wait/kill — guest session otherwise gets Access Denied.
            TryEnableDebugPrivilege(Log);

            if (opts.WaitPid is > 0)
                WaitForShellExit(opts.WaitPid.Value, Log);

            KillAllShell(Log, includeKeeper: false);
            Thread.Sleep(800);
            KillAllShell(Log, includeKeeper: false);
            // Let file handles settle on diskless write-cache.
            Thread.Sleep(400);

            if (!string.IsNullOrWhiteSpace(opts.ExpectedSha256))
            {
                var actual = ComputeSha256(opts.PackagePath);
                if (!string.Equals(actual, opts.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"SHA256 mismatch. expected={opts.ExpectedSha256} actual={actual}");
                Log("SHA256 OK");
            }

            // Stream zip entries straight into target — full extract to %TEMP% OOMs on CCBoot write-cache.
            TouchUpdatingLock();
            using (var suppress = new ShellSuppressor(Log))
            {
                ExtractZipToTarget(opts.PackagePath, opts.TargetDir, Log);
            }
            Log("Files copied.");

            // Подхватить новый ShiftClub.Client.Service.exe (иначе в памяти старый watchdog).
            TryReloadClientService(opts.TargetDir, Log);

            var launch = string.IsNullOrWhiteSpace(opts.LaunchExe)
                ? Path.Combine(opts.TargetDir, "ShiftClub.Client.Shell.exe")
                : opts.LaunchExe;

            if (!File.Exists(launch))
                throw new FileNotFoundException("Launch exe not found", launch);

            ClearUpdatingLock();
            Log($"Starting {launch}");
            TryStartShell(launch, opts.TargetDir, Log);

            Log("Done.");
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(UpdatesDir);
                File.AppendAllText(
                    Path.Combine(UpdatesDir, "updater.log"),
                    $"{DateTimeOffset.Now:O} ERROR {ex}{Environment.NewLine}");
            }
            catch
            {
                // ignore
            }

            // Always clear lock so Keeper can revive Shell — a stuck lock looks like a client "crash".
            ClearUpdatingLock();
            try
            {
                var fallback = @"D:\Apps\ShiftClub\Shell\ShiftClub.Client.Shell.exe";
                if (File.Exists(fallback))
                {
                    TryStartShell(fallback, Path.GetDirectoryName(fallback)!, msg =>
                    {
                        try
                        {
                            File.AppendAllText(
                                Path.Combine(UpdatesDir, "updater.log"),
                                $"{DateTimeOffset.Now:O} recovery: {msg}{Environment.NewLine}");
                        }
                        catch { /* ignore */ }
                    });
                }
            }
            catch { /* ignore */ }

            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void TryReloadClientService(string targetDir, Action<string> log)
    {
        try
        {
            var serviceExe = Path.Combine(targetDir, "service", "ShiftClub.Client.Service.exe");
            if (!File.Exists(serviceExe))
                serviceExe = Path.Combine(targetDir, "ShiftClub.Client.Service.exe");

            if (File.Exists(serviceExe))
            {
                // Обновить binPath на актуальный exe с Games-диска.
                RunSc($"config ShiftClubClient binPath= \"{serviceExe}\"", log);
            }

            // Попросить уже работающую службу (если ≥0.6.79) перезапуститься самой.
            try
            {
                ShiftClub.Shared.ClientIpc.WatchdogRequests.WriteRequest(
                    ShiftClub.Shared.ClientIpc.WatchdogRequests.ReloadService);
                log("Wrote reload-service.request for ShiftClubClient");
            }
            catch (Exception ex)
            {
                log($"reload-service.request failed: {ex.Message}");
            }

            // Fallback: прямой sc (сработает если Updater elevated / guest has rights).
            RunSc("stop ShiftClubClient", log);
            Thread.Sleep(1500);
            RunSc("start ShiftClubClient", log);
        }
        catch (Exception ex)
        {
            log($"TryReloadClientService: {ex.Message}");
        }
    }

    private static void RunSc(string args, Action<string> log)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("sc.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (p is null) return;
            var output = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim();
            p.WaitForExit(20_000);
            log($"sc {args} => {p.ExitCode} {output}");
        }
        catch (Exception ex)
        {
            log($"sc {args} error: {ex.Message}");
        }
    }

    private static void TryStartShell(string launch, string workingDir, Action<string> log)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = launch,
                WorkingDirectory = workingDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            log($"Start Shell failed: {ex.Message}");
        }
    }

    private static void WaitForShellExit(int waitPid, Action<string> log)
    {
        try
        {
            using var proc = Process.GetProcessById(waitPid);
            try { ProcessAccessGuard.UnprotectForUpdate(waitPid); }
            catch (Exception ex) { log($"UnprotectForUpdate: {ex.Message}"); }

            if (!proc.WaitForExit(45_000))
            {
                log("Shell did not exit in time, killing waitPid…");
                try
                {
                    ProcessAccessGuard.UnprotectForUpdate(waitPid);
                    proc.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    log($"Kill waitPid failed: {ex.Message}");
                }

                try { proc.WaitForExit(15_000); } catch { /* ignore */ }
            }
            else
            {
                log("waitPid exited cleanly.");
            }

            return;
        }
        catch (ArgumentException)
        {
            log("Shell process already exited.");
            return;
        }
        catch (Exception ex)
        {
            // Access Denied / race — do not abort the whole update.
            log($"waitPid open/wait failed ({ex.GetType().Name}: {ex.Message}); polling…");
        }

        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            var stillAlive = false;
            foreach (var p in Process.GetProcessesByName("ShiftClub.Client.Shell"))
            {
                try
                {
                    if (p.Id == waitPid)
                        stillAlive = true;
                    ProcessAccessGuard.UnprotectForUpdate(p.Id);
                    p.Kill(entireProcessTree: true);
                }
                catch { /* ignore */ }
                finally { p.Dispose(); }
            }

            if (!stillAlive && !Process.GetProcessesByName("ShiftClub.Client.Shell").Any())
            {
                log("Shell gone after poll.");
                return;
            }

            Thread.Sleep(500);
        }

        log("waitPid poll timed out — continuing with KillAllShell.");
    }

    private static void KillAllShell(Action<string> log, bool includeKeeper = false)
    {
        TryEnableDebugPrivilege(log);
        // Never kill Keeper during update — it honors updating.lock and must revive Shell after.
        var names = includeKeeper
            ? new[] { "ShiftClub.Client.Shell", "ShiftClub.Client.Keeper" }
            : new[] { "ShiftClub.Client.Shell" };
        foreach (var name in names)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    log($"Killing leftover {name} pid={p.Id}");
                    ProcessAccessGuard.UnprotectForUpdate(p.Id);
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(8_000);
                }
                catch (Exception ex)
                {
                    log($"Kill {name} failed: {ex.Message}");
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
    }

    private static void TryEnableDebugPrivilege(Action<string> log)
    {
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out var token)) // TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY
                return;
            try
            {
                if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out var luid))
                    return;
                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = new LUID_AND_ATTRIBUTES
                    {
                        Luid = luid,
                        Attributes = 0x00000002 // SE_PRIVILEGE_ENABLED
                    }
                };
                if (AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                    log("SeDebugPrivilege enabled for Shell kill");
            }
            finally
            {
                CloseHandle(token);
            }
        }
        catch (Exception ex)
        {
            log($"SeDebugPrivilege: {ex.Message}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState,
        int bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>
    /// While files are copied, keep killing Shell so an old Client.Service watchdog
    /// cannot restart it mid-overwrite (pre-0.6.40 services ignore updating.lock).
    /// </summary>
    private sealed class ShellSuppressor : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        public ShellSuppressor(Action<string> log)
        {
            _loop = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    try { KillAllShell(_ => { }, includeKeeper: false); } catch { /* ignore */ }
                    try { await Task.Delay(700, _cts.Token); } catch { break; }
                }
            });
            log("Shell suppressor started for copy phase");
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _loop.Wait(3_000); } catch { /* ignore */ }
            _cts.Dispose();
        }
    }

    private static void TouchUpdatingLock()
    {
        try
        {
            Directory.CreateDirectory(UpdatesDir);
            File.WriteAllText(UpdatingLockPath, $"{DateTimeOffset.UtcNow:O}|updater");
        }
        catch
        {
            /* ignore */
        }
    }

    private static void ClearUpdatingLock()
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

    private static void ExtractZipToTarget(string packagePath, string targetDir, Action<string> log)
    {
        Directory.CreateDirectory(targetDir);
        using var zip = ZipFile.OpenRead(packagePath);

        // Payload may be at zip root or under a single folder that contains Shell.exe.
        var shellEntry = zip.Entries.FirstOrDefault(e =>
            e.Name.Equals("ShiftClub.Client.Shell.exe", StringComparison.OrdinalIgnoreCase));
        var rootPrefix = "";
        if (shellEntry is not null)
        {
            var fullName = shellEntry.FullName.Replace('\\', '/');
            var slash = fullName.LastIndexOf('/');
            if (slash >= 0)
                rootPrefix = fullName[..(slash + 1)];
        }

        log($"Streaming zip → {targetDir} (prefix='{rootPrefix}')");

        foreach (var entry in zip.Entries)
        {
            var full = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrEmpty(entry.Name))
                continue; // directory marker

            string rel;
            if (string.IsNullOrEmpty(rootPrefix))
                rel = full;
            else if (full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                rel = full[rootPrefix.Length..];
            else
                continue; // skip sibling folders outside payload root

            if (string.IsNullOrWhiteSpace(rel))
                continue;

            rel = rel.Replace('/', Path.DirectorySeparatorChar);
            var dest = Path.Combine(targetDir, rel);

            if (rel.Equals("ShiftClub.Client.Updater.exe", StringComparison.OrdinalIgnoreCase)
                && IsSamePath(dest, Environment.ProcessPath))
            {
                log($"Skip self: {rel}");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            ExtractEntryWithRetry(entry, dest, log);
        }
    }

    private static void ExtractEntryWithRetry(ZipArchiveEntry entry, string dest, Action<string> log)
    {
        const int maxAttempts = 10;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                entry.ExtractToFile(dest, overwrite: true);
                return;
            }
            catch (IOException ex) when (attempt < maxAttempts)
            {
                log($"Extract locked ({attempt}/{maxAttempts}): {Path.GetFileName(dest)} — {ex.Message}");
                KillAllShell(log, includeKeeper: false);
                try
                {
                    var bak = dest + $".bak-{attempt}";
                    if (File.Exists(dest))
                        File.Move(dest, bak, overwrite: true);
                }
                catch { /* ignore */ }

                Thread.Sleep(350 * attempt);
            }
        }

        entry.ExtractToFile(dest, overwrite: true);
    }

    private static bool IsSamePath(string a, string? b)
    {
        if (string.IsNullOrWhiteSpace(b)) return false;
        return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

internal sealed class UpdateArgs
{
    public required string PackagePath { get; init; }
    public required string TargetDir { get; init; }
    public string? LaunchExe { get; init; }
    public int? WaitPid { get; init; }
    public string? ExpectedSha256 { get; init; }

    public static UpdateArgs Parse(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--job" && i + 1 < args.Length)
            {
                var json = File.ReadAllText(args[i + 1]);
                var job = JsonSerializer.Deserialize<UpdateJob>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? throw new InvalidOperationException("Invalid job file");
                return new UpdateArgs
                {
                    PackagePath = job.PackagePath,
                    TargetDir = job.TargetDir,
                    LaunchExe = job.LaunchExe,
                    WaitPid = job.WaitPid,
                    ExpectedSha256 = job.ExpectedSha256
                };
            }
        }

        string? Get(string name)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == name && i + 1 < args.Length)
                    return args[i + 1];
            }
            return null;
        }

        var package = Get("--package") ?? throw new ArgumentException("--package required");
        var target = Get("--target") ?? throw new ArgumentException("--target required");
        var waitRaw = Get("--wait-pid");
        return new UpdateArgs
        {
            PackagePath = package,
            TargetDir = target,
            LaunchExe = Get("--launch"),
            WaitPid = int.TryParse(waitRaw, out var pid) ? pid : null,
            ExpectedSha256 = Get("--sha256")
        };
    }
}

internal sealed class UpdateJob
{
    public string PackagePath { get; set; } = "";
    public string TargetDir { get; set; } = "";
    public string? LaunchExe { get; set; }
    public int? WaitPid { get; set; }
    public string? ExpectedSha256 { get; set; }
}
