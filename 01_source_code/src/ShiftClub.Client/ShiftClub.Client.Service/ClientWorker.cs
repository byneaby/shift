using System.Diagnostics;
using ShiftClub.Shared.ClientIpc;
using ShiftClub.Shared.Security;

namespace ShiftClub.Client.Service;

/// <summary>
/// Elevated watchdog (SYSTEM): process DACL + NVIDIA Display Container bounce.
/// Does NOT start Shell (Keeper owns session restart).
/// </summary>
public sealed class ClientWorker : BackgroundService
{
    public const string ServiceName = "ShiftClubClient";
    public const string CanonicalShellDir = @"D:\Apps\ShiftClub\Shell";
    public const string CanonicalShellExe = CanonicalShellDir + @"\ShiftClub.Client.Shell.exe";

    private static readonly string[] NvidiaContainerNames =
    [
        "NVDisplay.ContainerLocalSystem",
        "NVDisplayContainerLocalSystem",
        "NvContainerLocalSystem"
    ];

    private readonly ILogger<ClientWorker> _logger;
    private readonly IConfiguration _configuration;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastServiceRecoveryEnsure = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAclRefresh = DateTimeOffset.MinValue;
    private bool _serviceRecoveryConfigured;
    private int _nvDelayedPhase;

    public ClientWorker(ILogger<ClientWorker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "SHIFT Club Client watchdog started (ACL + NVIDIA container; Shell via Keeper)");
        EnsureWindowsServiceRecovery();
        try
        {
            LauncherRegistryFix.Apply();
            _logger.LogInformation("Launcher registry ACL/InstallPath applied");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Launcher registry fix failed");
        }

        // CCBoot Ð±ÐµÐ· SuperClient: Ð½Ðµ Ð´Ð°Ð²Ð°Ñ‚ÑŒ ÐºÐ¾Ð½Ñ‚ÐµÐ¹Ð½ÐµÑ€Ñƒ ÑÑ‚Ð°Ñ€Ñ‚Ð¾Ð²Ð°Ñ‚ÑŒ Ð·Ð¾Ð¼Ð±Ð¸ Ð¿Ñ€Ð¸ Ð±ÑƒÑ‚Ðµ;
        // Ð¿Ð¾Ð´Ð½ÑÑ‚ÑŒ ÐµÐ³Ð¾ Ñ‚Ð¾Ð»ÑŒÐºÐ¾ Ð¿Ð¾ÑÐ»Ðµ NVIDIA GPU (Ñ„Ð¾Ð½Ð¾Ð¼, Ð½Ðµ Ð±Ð»Ð¾ÐºÐ¸Ñ€ÑƒÑ Ñ†Ð¸ÐºÐ»).
        _ = Task.Run(() =>
        {
            try
            {
                EnsureNvidiaContainerDemandStart();
                if (WaitForNvidiaGpu(TimeSpan.FromMinutes(8)))
                {
                    RestartNvidiaDisplayContainer();
                    Thread.Sleep(30_000);
                    RestartNvidiaDisplayContainer();
                }
                else
                {
                    _logger.LogWarning("NVIDIA GPU not seen in 8 min â€” bounce anyway");
                    RestartNvidiaDisplayContainer();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Boot NVIDIA container fix failed");
            }
        });

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                ProtectRunningShellProcesses();
                PeriodicallyEnsureServiceRecovery();
                ProcessWatchdogRequests();
                MaybeDelayedNvidiaRestart();
                MaybeSelfReload();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Shell watchdog error");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private void ProcessWatchdogRequests()
    {
        if (!WatchdogRequests.TryTakeRequest(WatchdogRequests.RestartNvContainer))
            return;

        _ = Task.Run(() =>
        {
            try
            {
                WaitForNvidiaGpu(TimeSpan.FromSeconds(120));
                var ok = RestartNvidiaDisplayContainer();
                WatchdogRequests.WriteAck(
                    WatchdogRequests.RestartNvContainer,
                    ok ? "ok" : "failed");
                _logger.LogInformation("NVIDIA container bounce via Shell request: {Ok}", ok);
            }
            catch (Exception ex)
            {
                WatchdogRequests.WriteAck(WatchdogRequests.RestartNvContainer, "error:" + ex.Message);
                _logger.LogWarning(ex, "NVIDIA container bounce request failed");
            }
        });
    }

    private void EnsureNvidiaContainerDemandStart()
    {
        foreach (var name in NvidiaContainerNames)
        {
            if (!ServiceExists(name)) continue;
            try
            {
                RunSc($"config \"{name}\" start= demand");
                _logger.LogInformation("Set {Service} start=demand", name);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "config demand {Service}", name);
            }
        }
    }

    /// <summary>
    /// ÐŸÐ¾ÑÐ»Ðµ OTA Updater ÐºÐ»Ð°Ð´Ñ‘Ñ‚ reload-service.request â€” ÑÐ»ÑƒÐ¶Ð±Ð° Ð¿ÐµÑ€ÐµÐ·Ð°Ð¿ÑƒÑÐºÐ°ÐµÑ‚ ÑÐµÐ±Ñ,
    /// Ñ‡Ñ‚Ð¾Ð±Ñ‹ Ð¿Ð¾Ð´Ñ…Ð²Ð°Ñ‚Ð¸Ñ‚ÑŒ Ð½Ð¾Ð²Ñ‹Ð¹ exe (Ð¸Ð½Ð°Ñ‡Ðµ Ð² Ð¿Ð°Ð¼ÑÑ‚Ð¸ Ð¾ÑÑ‚Ð°Ñ‘Ñ‚ÑÑ ÑÑ‚Ð°Ñ€Ñ‹Ð¹ ÐºÐ¾Ð´).
    /// </summary>
    private void MaybeSelfReload()
    {
        if (!WatchdogRequests.TryTakeRequest(WatchdogRequests.ReloadService))
            return;

        try
        {
            _logger.LogInformation("Self-reload requested â€” restarting {Service}", ServiceName);
            WatchdogRequests.WriteAck(WatchdogRequests.ReloadService, "reloading");
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c timeout /t 2 /nobreak >nul & sc start {ServiceName}",
                CreateNoWindow = true,
                UseShellExecute = false
            })?.Dispose();
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Self-reload failed");
        }
    }

    /// <summary>
    /// Ð‘ÐµÐ· SuperClient InfGPU Ð¾Ñ‚Ð´Ð°Ñ‘Ñ‚ GPU Ð¿Ð¾Ð·Ð´Ð½Ð¾. Delayed bounce Ñ‚Ð¾Ð»ÑŒÐºÐ¾ ÐŸÐžÐ¡Ð›Ð• GPU,
    /// Ð¸Ð½Ð°Ñ‡Ðµ ÑÐ½Ð¾Ð²Ð° Ð¿Ð¾Ð»ÑƒÑ‡Ð¸Ð¼ Ð·Ð¾Ð¼Ð±Ð¸ ÐºÐ°Ðº Ð¿Ñ€Ð¸ Automatic-ÑÑ‚Ð°Ñ€Ñ‚Ðµ.
    /// </summary>
    private void MaybeDelayedNvidiaRestart()
    {
        var elapsed = DateTimeOffset.UtcNow - _startedAt;
        // ÐŸÐµÑ€Ð²Ð°Ñ Ð¿Ð¾Ð¿Ñ‹Ñ‚ÐºÐ° Ð½Ðµ Ñ€Ð°Ð½ÑŒÑˆÐµ ~45Ñ Ð¸ Ñ‚Ð¾Ð»ÑŒÐºÐ¾ ÐµÑÐ»Ð¸ GPU ÑƒÐ¶Ðµ Ð²Ð¸Ð´ÐµÐ½ (Ð¸Ð»Ð¸ Ð¶Ð´Ñ‘Ð¼).
        if (_nvDelayedPhase == 0 && elapsed >= TimeSpan.FromSeconds(45))
        {
            if (WaitForNvidiaGpu(TimeSpan.FromSeconds(60)))
            {
                RestartNvidiaDisplayContainer();
                _nvDelayedPhase = 1;
            }
            else if (elapsed >= TimeSpan.FromMinutes(3))
            {
                RestartNvidiaDisplayContainer();
                _nvDelayedPhase = 1;
            }
        }
        else if (_nvDelayedPhase == 1 && elapsed >= TimeSpan.FromMinutes(2))
        {
            RestartNvidiaDisplayContainer();
            _nvDelayedPhase = 2;
        }
    }

    private bool RestartNvidiaDisplayContainer()
    {
        // 1) Ð£Ð±Ð¸Ñ‚ÑŒ Ð·Ð¾Ð¼Ð±Ð¸-Ð¿Ñ€Ð¾Ñ†ÐµÑÑÑ‹ â€” Ð¸Ð¼ÐµÐ½Ð½Ð¾ ÑÑ‚Ð¾ Ð¿Ð¾Ð¼Ð¾Ð³Ð°ÐµÑ‚ Ð²Ñ€ÑƒÑ‡Ð½ÑƒÑŽ Ð½Ð° ÐºÐ»ÑƒÐ±Ðµ.
        KillNvidiaContainerProcesses();

        // 2) ÐžÑ„Ð¸Ñ†Ð¸Ð°Ð»ÑŒÐ½Ñ‹Ð¹ recovery.bat Ð¸Ð· DriverStore, ÐµÑÐ»Ð¸ ÐµÑÑ‚ÑŒ.
        TryRunNvContainerRecovery();

        // 3) stop/start ÑÐ»ÑƒÐ¶Ð±Ñ‹ â€” Ð¿Ð¾Ð´Ð½Ð¸Ð¼ÐµÑ‚ Ð½Ð¾Ð²Ñ‹Ð¹ NVDisplay.Container.exe.
        var serviceOk = false;
        foreach (var name in NvidiaContainerNames)
        {
            if (!ServiceExists(name))
                continue;

            try
            {
                _logger.LogInformation("Restarting NVIDIA container service {Service}", name);
                RunSc($"stop \"{name}\"");
                WaitForServiceState(name, running: false, TimeSpan.FromSeconds(12));
                KillNvidiaContainerProcesses();
                Thread.Sleep(500);
                RunSc($"start \"{name}\"");
                serviceOk = WaitForServiceState(name, running: true, TimeSpan.FromSeconds(20));
                if (serviceOk)
                {
                    _logger.LogInformation("NVIDIA container service {Service} is RUNNING", name);
                    break;
                }

                _logger.LogWarning("NVIDIA container service {Service} did not reach RUNNING", name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "NVIDIA container service {Service} restart error", name);
            }
        }

        if (!serviceOk)
            TryStartContainerExeDirect();

        Thread.Sleep(2500);
        var alive = Process.GetProcessesByName("NVDisplay.Container").Length > 0
                    || Process.GetProcessesByName("NVDisplayContainer").Length > 0;
        _logger.LogInformation("NVIDIA container process alive after bounce: {Alive}", alive);
        return alive || serviceOk;
    }

    private bool WaitForNvidiaGpu(TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try
            {
                // Display class GUID â€” Ð±ÐµÐ· Ð·Ð°Ð²Ð¸ÑÐ¸Ð¼Ð¾ÑÑ‚Ð¸ Ð¾Ñ‚ System.Management.
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (key is not null)
                {
                    foreach (var name in key.GetSubKeyNames())
                    {
                        if (!char.IsDigit(name[0])) continue;
                        using var sk = key.OpenSubKey(name);
                        var desc = sk?.GetValue("DriverDesc") as string ?? "";
                        if (desc.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
                            || desc.Contains("GeForce", StringComparison.OrdinalIgnoreCase)
                            || desc.Contains("RTX", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("NVIDIA GPU ready: {Name}", desc);
                            return true;
                        }
                    }
                }
            }
            catch { /* registry may flap during PnP */ }

            Thread.Sleep(2000);
        }

        return false;
    }

    private void KillNvidiaContainerProcesses()
    {
        foreach (var name in new[] { "NVDisplay.Container", "NVDisplayContainer" })
        {
            try
            {
                RunProcess("taskkill.exe", $"/F /IM \"{name}.exe\" /T", 8000);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "taskkill {Name} failed", name);
            }

            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(3000);
                }
                catch { /* ignore */ }
                finally { p.Dispose(); }
            }
        }
    }

    private void TryRunNvContainerRecovery()
    {
        try
        {
            var bat = FindNvContainerRecoveryBat();
            if (bat is null) return;
            _logger.LogInformation("Running NvContainerRecovery: {Path}", bat);
            RunProcess("cmd.exe", $"/c \"\"{bat}\"\"", 20000);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "NvContainerRecovery failed");
        }
    }

    private void TryStartContainerExeDirect()
    {
        try
        {
            var exe = FindNvContainerExe();
            if (exe is null) return;
            _logger.LogInformation("Starting NVDisplay.Container.exe directly: {Path}", exe);
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
                UseShellExecute = false,
                CreateNoWindow = true
            })?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Direct container start failed");
        }
    }

    private static string? FindNvContainerExe()
    {
        string[] roots =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "DriverStore", "FileRepository"),
            @"C:\Windows\System32\DriverStore\FileRepository",
            @"C:\CCBoot\InfGPU"
        ];
        foreach (var root in roots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                var hit = Directory.EnumerateFiles(root, "NVDisplay.Container.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(hit)) return hit;
            }
            catch { /* ignore */ }
        }

        return null;
    }

    private static string? FindNvContainerRecoveryBat()
    {
        var exe = FindNvContainerExe();
        if (exe is not null)
        {
            var bat = Path.Combine(Path.GetDirectoryName(exe)!, "NvContainerRecovery.bat");
            if (File.Exists(bat)) return bat;
        }

        try
        {
            const string inf = @"C:\CCBoot\InfGPU";
            if (Directory.Exists(inf))
                return Directory.EnumerateFiles(inf, "NvContainerRecovery.bat", SearchOption.AllDirectories)
                    .FirstOrDefault();
        }
        catch { /* ignore */ }

        return null;
    }

    private static void RunProcess(string fileName, string args, int timeoutMs)
    {
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        p?.WaitForExit(timeoutMs);
    }

    private static bool ServiceExists(string serviceName)
    {
        try
        {
            using var q = Process.Start(new ProcessStartInfo("sc.exe", $"query \"{serviceName}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (q is null) return false;
            var output = q.StandardOutput.ReadToEnd();
            q.WaitForExit(4000);
            return q.ExitCode == 0 && output.Contains("SERVICE_NAME", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool WaitForServiceState(string serviceName, bool running, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var state = QueryServiceState(serviceName);
            if (running)
            {
                if (state is 4) return true;
            }
            else if (state is 1 or null)
            {
                return true;
            }

            Thread.Sleep(250);
        }

        var final = QueryServiceState(serviceName);
        return running ? final == 4 : final is 1 or null;
    }

    private static int? QueryServiceState(string serviceName)
    {
        try
        {
            using var q = Process.Start(new ProcessStartInfo("sc.exe", $"query \"{serviceName}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (q is null) return null;
            var output = q.StandardOutput.ReadToEnd();
            q.WaitForExit(4000);
            if (q.ExitCode != 0) return null;

            foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (!t.StartsWith("STATE", StringComparison.OrdinalIgnoreCase))
                    continue;
                var parts = t.Split(':', 2);
                if (parts.Length < 2) continue;
                var nums = parts[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (nums.Length > 0 && int.TryParse(nums[0], out var code))
                    return code;
            }
        }
        catch { /* ignore */ }

        return null;
    }

    private void ProtectRunningShellProcesses()
    {
        if (IsUpdateInProgress() || ClientAdminMode.IsActive())
            return;

        if (DateTimeOffset.UtcNow - _lastAclRefresh < TimeSpan.FromSeconds(10))
            return;
        _lastAclRefresh = DateTimeOffset.UtcNow;

        foreach (var name in new[] { "ShiftClub.Client.Shell", "ShiftClub.Client.Keeper" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    ProcessAccessGuard.ProtectProcess(p.Id);
                }
                catch
                {
                    /* ignore */
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
    }

    private static bool IsUpdateInProgress()
    {
        try
        {
            var lockPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ShiftClub",
                "Client",
                "updates",
                "updating.lock");
            if (!File.Exists(lockPath))
                return false;

            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath);
            if (age > TimeSpan.FromMinutes(15))
            {
                try { File.Delete(lockPath); } catch { /* ignore stale */ }
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void PeriodicallyEnsureServiceRecovery()
    {
        if (_serviceRecoveryConfigured && DateTimeOffset.UtcNow - _lastServiceRecoveryEnsure < TimeSpan.FromMinutes(30))
            return;
        _lastServiceRecoveryEnsure = DateTimeOffset.UtcNow;
        EnsureWindowsServiceRecovery();
    }

    private void EnsureWindowsServiceRecovery()
    {
        try
        {
            RunSc($"failure {ServiceName} reset= 86400 actions= restart/3000/restart/3000/restart/5000");
            RunSc($"failureflag {ServiceName} 1");
            _serviceRecoveryConfigured = true;
            _logger.LogInformation("Windows service recovery configured for {Service}", ServiceName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not set service recovery");
        }
    }

    private static void RunSc(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("sc.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        p?.WaitForExit(20_000);
    }
}
