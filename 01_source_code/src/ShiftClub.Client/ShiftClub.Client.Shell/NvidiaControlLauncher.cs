using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Windows;
using Microsoft.Win32;
using ShiftClub.Shared.ClientIpc;

namespace ShiftClub.Client.Shell;

/// <summary>NVIDIA Control Panel — classic exe, AppX package, or CCBoot InfGPU staging.</summary>
internal static class NvidiaControlLauncher
{
    public static string DisplayName => "NVIDIA";
    public static string ProcessName => "nvcplui";

    private static string? _cachedExe;
    private static string? _cachedAumid;
    private static DateTime _cacheAt;
    private static DateTime _lastEnsureUtc = DateTime.MinValue;

    public static string? ResolveExecutable()
    {
        if (!string.IsNullOrWhiteSpace(_cachedExe)
            && File.Exists(_cachedExe)
            && DateTime.UtcNow - _cacheAt < TimeSpan.FromMinutes(10))
            return _cachedExe;

        foreach (var path in CandidatePaths())
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    return CacheExe(path);
            }
            catch { /* ignore */ }
        }

        return CacheExe(
            FindViaAppPaths()
            ?? FindUnderNvidiaFolders()
            ?? FindUnderCcbootInfGpu()
            ?? FindViaWhere());
    }

    public static bool TryLaunch()
    {
        try
        {
            // На бездисковых образах CPL часто «молчит», пока не перезапустить контейнер NVIDIA.
            EnsureDisplayContainerReady();

            var exe = ResolveExecutable();
            if (!string.IsNullOrWhiteSpace(exe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
                    UseShellExecute = true
                });
                return true;
            }

            if (TryLaunchAppx())
                return true;

            // Windows display settings — usable fallback for resolution / Hz.
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true });
                return true;
            }
            catch { /* ignore */ }

            MessageBox.Show(
                "Панель NVIDIA не найдена на этом ПК.\nОткрыты параметры экрана Windows.",
                "NVIDIA",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "NVIDIA", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>
    /// Просит ShiftClubClient (SYSTEM) убить NVDisplay.Container и поднять службу.
    /// Не блокирует UI надолго — ack ждём коротко; повторные запросы шлёт ScheduleFixOnShellLaunch.
    /// </summary>
    public static void EnsureDisplayContainerReady()
    {
        RequestFixNow(waitAck: true, ackTimeout: TimeSpan.FromSeconds(8));
    }

    /// <summary>
    /// При старте Shell: несколько запросов с паузами (InfGPU GPU появляется с задержкой).
    /// </summary>
    public static void ScheduleFixOnShellLaunch()
    {
        _ = Task.Run(async () =>
        {
            // Абсолютные метки от старта Shell — InfGPU GPU появляется с задержкой.
            int[] atSec = [2, 20, 60, 150];
            var started = DateTime.UtcNow;
            foreach (var sec in atSec)
            {
                try
                {
                    var wait = TimeSpan.FromSeconds(sec) - (DateTime.UtcNow - started);
                    if (wait > TimeSpan.Zero)
                        await Task.Delay(wait).ConfigureAwait(false);
                    // Сброс throttle между слотами (иначе 5с-лимит съест повтор).
                    _lastEnsureUtc = DateTime.MinValue;
                    RequestFixNow(waitAck: false, ackTimeout: TimeSpan.Zero);
                }
                catch { /* ignore */ }
            }
        });
    }

    private static void RequestFixNow(bool waitAck, TimeSpan ackTimeout)
    {
        // Не чаще раза в 5с — служба может ещё обрабатывать предыдущий bounce.
        if (DateTime.UtcNow - _lastEnsureUtc < TimeSpan.FromSeconds(5))
            return;
        _lastEnsureUtc = DateTime.UtcNow;

        try
        {
            WatchdogRequests.WriteRequest(WatchdogRequests.RestartNvContainer);
            if (waitAck && ackTimeout > TimeSpan.Zero)
                WatchdogRequests.WaitForAck(WatchdogRequests.RestartNvContainer, ackTimeout);
        }
        catch { /* ignore */ }

        // Fallback только если Shell elevated (редко на киоске).
        try
        {
            foreach (var name in new[]
                     {
                         "NVDisplay.ContainerLocalSystem",
                         "NVDisplayContainerLocalSystem",
                         "NvContainerLocalSystem"
                     })
            {
                if (TryRestartServiceLocal(name))
                    return;
            }
        }
        catch { /* best-effort */ }
    }

    private static bool TryRestartServiceLocal(string serviceName)
    {
        try
        {
            if (!ServiceExists(serviceName))
                return false;

            var startCode = -1;
            RunSc($"stop \"{serviceName}\"", 12000);
            Thread.Sleep(900);
            startCode = RunSc($"start \"{serviceName}\"", 12000);
            Thread.Sleep(2000);
            return startCode == 0 || startCode == 1056 || ServiceIsRunning(serviceName);
        }
        catch
        {
            return false;
        }
    }

    private static bool ServiceExists(string serviceName)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query \"{serviceName}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (p is null) return false;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(4000);
            return p.ExitCode == 0 && output.Contains("SERVICE_NAME", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool ServiceIsRunning(string serviceName)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query \"{serviceName}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (p is null) return false;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(4000);
            return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static int RunSc(string args, int timeoutMs)
    {
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        if (p is null) return -1;
        p.WaitForExit(timeoutMs);
        return p.ExitCode;
    }

    private static bool TryLaunchAppx()
    {
        foreach (var aumid in EnumerateAumids())
        {
            try
            {
                // explorer shell:AppsFolder\<AUMID>
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"shell:AppsFolder\\{aumid}",
                    UseShellExecute = true
                });
                _cachedAumid = aumid;
                _cacheAt = DateTime.UtcNow;
                return true;
            }
            catch { /* try next */ }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"shell:AppsFolder\\{aumid}",
                    UseShellExecute = true
                });
                _cachedAumid = aumid;
                _cacheAt = DateTime.UtcNow;
                return true;
            }
            catch { /* try next */ }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateAumids()
    {
        if (!string.IsNullOrWhiteSpace(_cachedAumid))
            yield return _cachedAumid;

        // Known publisher ids seen in the wild (NVIDIA Control Panel AppX).
        yield return "NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj!NVIDIAControlPanel";
        yield return "NVIDIACorp.NVIDIAControlPanel_56jnetd0y3y40!NVIDIAControlPanel";
        yield return "NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj!App";
        yield return "NVIDIACorp.NVIDIAControlPanel_56jnetd0y3y40!App";

        foreach (var aumid in DiscoverAumidsFromDisk())
            yield return aumid;
    }

    private static IEnumerable<string> DiscoverAumidsFromDisk()
    {
        string[] roots =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps"),
            @"C:\Program Files\WindowsApps",
            @"C:\CCBoot\InfGPU"
        ];

        foreach (var root in roots)
        {
            IEnumerable<string> dirs;
            try
            {
                if (!Directory.Exists(root))
                    continue;
                dirs = Directory.EnumerateDirectories(root, "NVIDIACorp.NVIDIAControlPanel_*", SearchOption.AllDirectories)
                    .Take(8);
            }
            catch
            {
                continue;
            }

            foreach (var dir in dirs)
            {
                var aumid = TryReadAumid(dir);
                if (!string.IsNullOrWhiteSpace(aumid))
                    yield return aumid!;

                var exe = Path.Combine(dir, "nvcplui.exe");
                if (File.Exists(exe))
                    CacheExe(exe);
            }
        }
    }

    private static string? TryReadAumid(string packageDir)
    {
        try
        {
            var manifest = Path.Combine(packageDir, "AppxManifest.xml");
            if (!File.Exists(manifest))
                return null;

            var folderName = Path.GetFileName(packageDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            // Folder: NVIDIACorp.NVIDIAControlPanel_8.1.968.0_x64__56jybvy8sckqj
            var parts = folderName.Split("__");
            if (parts.Length < 2)
                return null;
            var publisher = parts[^1];
            var nameVer = parts[0]; // NVIDIACorp.NVIDIAControlPanel_8.1.968.0_x64
            var name = nameVer.Split('_')[0]; // NVIDIACorp.NVIDIAControlPanel
            var family = $"{name}_{publisher}";

            var doc = XDocument.Load(manifest);
            XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
            var appId = doc.Descendants(ns + "Application")
                .Select(a => (string?)a.Attribute("Id"))
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id))
                ?? "App";

            return $"{family}!{appId}";
        }
        catch
        {
            return null;
        }
    }

    private static string? CacheExe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        _cachedExe = path;
        _cacheAt = DateTime.UtcNow;
        return path;
    }

    private static string? FindViaAppPaths()
    {
        string[] keys =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\nvcplui.exe",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\nvcplui.exe"
        ];

        foreach (var keyPath in keys)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                var def = key?.GetValue(null) as string;
                if (!string.IsNullOrWhiteSpace(def))
                {
                    var path = Environment.ExpandEnvironmentVariables(def.Trim().Trim('"'));
                    if (File.Exists(path))
                        return path;
                }
            }
            catch { /* ignore */ }
        }

        return null;
    }

    private static string? FindUnderNvidiaFolders()
    {
        string[] roots =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NVIDIA Corporation"),
            @"C:\Program Files\NVIDIA Corporation",
            @"C:\Program Files (x86)\NVIDIA Corporation"
        ];

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(root))
                    continue;
                var hit = Directory.EnumerateFiles(root, "nvcplui.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(hit))
                    return hit;
            }
            catch { /* ignore */ }
        }

        return null;
    }

    private static string? FindUnderCcbootInfGpu()
    {
        try
        {
            const string root = @"C:\CCBoot\InfGPU";
            if (!Directory.Exists(root))
                return null;
            return Directory.EnumerateFiles(root, "nvcplui.exe", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? FindViaWhere()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = "nvcplui.exe",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            using var p = Process.Start(psi);
            if (p is null)
                return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            var line = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(line) && File.Exists(line.Trim()))
                return line.Trim();
        }
        catch { /* ignore */ }

        return null;
    }

    private static IEnumerable<string> CandidatePaths()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Path.Combine(pf, "NVIDIA Corporation", "Control Panel Client", "nvcplui.exe");
        yield return Path.Combine(pf86, "NVIDIA Corporation", "Control Panel Client", "nvcplui.exe");
        yield return @"C:\Program Files\NVIDIA Corporation\Control Panel Client\nvcplui.exe";
        yield return @"C:\Windows\System32\nvcplui.exe";
    }
}
