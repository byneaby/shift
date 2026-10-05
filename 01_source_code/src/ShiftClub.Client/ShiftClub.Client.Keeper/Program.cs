using System.Diagnostics;
using ShiftClub.Shared.Security;

namespace ShiftClub.Client.Keeper;

/// <summary>
/// Single user-session watchdog: starts Shell if missing. One Shell only (Shell has mutex).
/// Do not put Shell itself in Startup — only this Keeper.
/// </summary>
internal static class Program
{
    private const string ShellName = "ShiftClub.Client.Shell";
    private static readonly string CanonicalShell =
        @"D:\Apps\ShiftClub\Shell\ShiftClub.Client.Shell.exe";

    private static int Main()
    {
        // Avoid racing Startup/Service on first seconds after logon.
        Thread.Sleep(2500);

        var shellExe = ResolveShellExe();
        var lastStart = DateTime.MinValue;

        while (true)
        {
            try
            {
                // Senet/iCafe-style: never revive Shell while staff admin.mode is set.
                if (ClientAdminMode.IsActive())
                {
                    Thread.Sleep(2000);
                    continue;
                }

                if (!IsUpdateInProgress() && !IsRunning(ShellName))
                {
                    // Debounce: don't spam-start if Shell crashes instantly.
                    if ((DateTime.UtcNow - lastStart).TotalSeconds >= 3)
                    {
                        lastStart = DateTime.UtcNow;
                        StartShell(shellExe);
                    }
                }
            }
            catch
            {
                /* ignore */
            }

            Thread.Sleep(1500);
        }
    }

    private static bool IsRunning(string name)
    {
        var procs = Process.GetProcessesByName(name);
        try
        {
            return procs.Length > 0;
        }
        finally
        {
            foreach (var p in procs) p.Dispose();
        }
    }

    private static void StartShell(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            return;
        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = true
        });
    }

    private static string? ResolveShellExe()
    {
        var dir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(dir, ShellName + ".exe"),
            CanonicalShell,
            Path.Combine(dir, "..", ShellName + ".exe"),
            @"D:\01 SHIFT\Shell\ShiftClub.Client.Shell.exe"
        };
        return candidates.Select(p => Path.GetFullPath(p)).FirstOrDefault(File.Exists);
    }

    private static bool IsUpdateInProgress()
    {
        try
        {
            var lockPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ShiftClub", "Client", "updates", "updating.lock");
            if (!File.Exists(lockPath))
                return false;
            return DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath) < TimeSpan.FromMinutes(15);
        }
        catch
        {
            return false;
        }
    }
}
