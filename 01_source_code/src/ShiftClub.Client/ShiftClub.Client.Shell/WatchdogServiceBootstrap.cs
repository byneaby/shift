using System.Diagnostics;
using System.IO;

namespace ShiftClub.Client.Shell;

/// <summary>
/// Ensures ShiftClubClient Windows service exists and is running (watchdog).
/// Without this, killing Shell leaves the PC without a restart loop.
/// </summary>
internal static class WatchdogServiceBootstrap
{
    public const string ServiceName = "ShiftClubClient";

    public static void EnsureInstalledAndRunning()
    {
        try
        {
            var exe = ResolveServiceExe();
            if (exe is null || !File.Exists(exe))
                return;

            if (!ServiceExists())
            {
                // sc.exe requires: binPath= <path> with a space after = and no extra quotes if path has no spaces
                RunSc($"create {ServiceName} binPath= {exe} start= auto DisplayName= SHIFT_Club_Client_Watchdog");
                RunSc($"description {ServiceName} Restarts_SHIFT_Club_Shell");
            }

            RunSc($"config {ServiceName} binPath= {exe}");
            RunSc($"failure {ServiceName} reset= 86400 actions= restart/3000/restart/3000/restart/5000");
            RunSc($"failureflag {ServiceName} 1");
            RunSc($"start {ServiceName}");
        }
        catch
        {
            /* not elevated */
        }
    }

    private static string? ResolveServiceExe()
    {
        var dir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(dir, "service", "ShiftClub.Client.Service.exe"),
            Path.Combine(dir, "ShiftClub.Client.Service.exe"),
            @"D:\Apps\ShiftClub\Shell\service\ShiftClub.Client.Service.exe",
            @"D:\01 SHIFT\Shell\service\ShiftClub.Client.Service.exe"
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool ServiceExists()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("sc.exe", $"query {ServiceName}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (p is null) return false;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10_000);
            return p.ExitCode == 0 && output.Contains("SERVICE_NAME", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
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
