using System.Diagnostics;
using System.IO;
using ShiftClub.Shared.Security;

namespace ShiftClub.Client.Shell;

/// <summary>Starts / keeps the user-session Keeper process as a fallback watchdog.</summary>
internal static class KeeperBootstrap
{
    public static void EnsureRunning()
    {
        try
        {
            if (Process.GetProcessesByName("ShiftClub.Client.Keeper").Length > 0)
                return;

            var exe = ResolveKeeperExe();
            if (exe is null)
                return;

            var p = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = true
            });
            if (p is not null)
            {
                try
                {
                    Thread.Sleep(300);
                    ProcessAccessGuard.ProtectProcess(p.Id);
                }
                finally
                {
                    p.Dispose();
                }
            }

            EnsureStartupShortcut(exe);
        }
        catch
        {
            /* ignore */
        }
    }

    private static string? ResolveKeeperExe()
    {
        var dir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(dir, "ShiftClub.Client.Keeper.exe"),
            Path.Combine(dir, "keeper", "ShiftClub.Client.Keeper.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void EnsureStartupShortcut(string exe)
    {
        try
        {
            var startup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            if (string.IsNullOrWhiteSpace(startup) || !Directory.Exists(startup))
                startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (string.IsNullOrWhiteSpace(startup))
                return;

            var lnk = Path.Combine(startup, "SHIFT Club Keeper.lnk");
            if (File.Exists(lnk))
                return;

            // Minimal .lnk via PowerShell (no COM dependency issues in self-contained)
            var ps = $"$w=New-Object -ComObject WScript.Shell; $s=$w.CreateShortcut('{lnk.Replace("'", "''")}'); $s.TargetPath='{exe.Replace("'", "''")}'; $s.WorkingDirectory='{Path.GetDirectoryName(exe)!.Replace("'", "''")}'; $s.WindowStyle=7; $s.Save()";
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -WindowStyle Hidden -Command \"{ps}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            })?.WaitForExit(10_000);
        }
        catch
        {
            /* ignore */
        }
    }
}
