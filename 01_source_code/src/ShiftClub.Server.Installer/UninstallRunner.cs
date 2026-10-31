using System.Diagnostics;
using ShiftClub.Server.Setup;

namespace ShiftClub.Server.Installer;

internal static class UninstallRunner
{
    public static string? DetectDirectory()
    {
        var candidates = new List<string>();
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exe))
        {
            var dir = Path.GetDirectoryName(exe);
            if (!string.IsNullOrWhiteSpace(dir))
                candidates.Add(dir);
        }

        candidates.Add(@"C:\ShiftClub\Server");
        foreach (var dir in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(ServerInstallMarker.FilePath(dir))
                || File.Exists(Path.Combine(dir, "ShiftClub.Server.exe"))
                || File.Exists(Path.Combine(dir, "secrets.env")))
                return dir;
        }

        return null;
    }

    public static ServerInstallMarker Describe(string directory)
    {
        var marker = ServerInstallMarker.Read(directory) ?? new ServerInstallMarker();
        marker.InstallDirectory = directory;
        if (marker.ListenPort is < 1 or > 65535)
            marker.ListenPort = 5080;
        if (string.IsNullOrWhiteSpace(marker.ClubName))
        {
            var brand = ShiftClub.Shared.Branding.ClubBrandingFiles.Read(directory);
            marker.ClubName = string.IsNullOrWhiteSpace(brand.Name) ? "Клуб" : brand.Name;
        }

        if (string.IsNullOrWhiteSpace(marker.FirewallRuleName))
            marker.FirewallRuleName = WindowsInstallPlan.FirewallRuleName(marker.ClubName, marker.ListenPort);
        if (!marker.BundledPostgres && Directory.Exists(PostgresBundle.DataDirectory))
            marker.BundledPostgres = true;
        return marker;
    }

    public static async Task<string?> RunAsync(ServerInstallMarker marker, bool removePostgres, Action<string> report, CancellationToken cancellationToken)
    {
        var dir = marker.InstallDirectory.Trim().TrimEnd('\\');
        if (!ServerSetupValidator.IsSafeInstallDirectory(dir))
            return "Эту папку удалять нельзя.";

        report("Останавливаю сервер…");
        foreach (var name in new[] { "ShiftClubApi", "ShiftClubApiWatchdog" })
        {
            await ServerInstallRunner.RunCodeAsync("schtasks.exe", $"/End /TN {name}", 15_000);
            await ServerInstallRunner.RunCodeAsync("schtasks.exe", $"/Delete /TN {name} /F", 15_000);
        }

        await ServerInstallRunner.RunCodeAsync("taskkill.exe", "/F /T /IM ShiftClub.Server.exe", 20_000);
        cancellationToken.ThrowIfCancellationRequested();

        report("Убираю правило брандмауэра…");
        foreach (var rule in ServerUninstallPlan.FirewallRulesToDelete(marker))
            await ServerInstallRunner.RunCodeAsync("netsh", ServerUninstallPlan.FirewallDeleteArguments(rule), 20_000);

        await ServerInstallRunner.RunCodeAsync("reg.exe", ServerUninstallPlan.DeleteRegistryArguments(), 15_000);

        if (ServerUninstallPlan.CanRemoveBundledPostgres(marker, removePostgres))
        {
            report("Останавливаю PostgreSQL…");
            await ServerInstallRunner.RunCodeAsync("sc.exe", "stop " + PostgresBundle.ServiceName, 30_000);
            await ServerInstallRunner.RunCodeAsync("sc.exe", "delete " + PostgresBundle.ServiceName, 20_000);
            await Task.Delay(1000, cancellationToken);
            try
            {
                if (Directory.Exists(PostgresBundle.InstallRoot))
                    Directory.Delete(PostgresBundle.InstallRoot, recursive: true);
            }
            catch (Exception)
            {
                return "Сервер снят, но папка PostgreSQL ещё занята. Закройте postgres.exe и удалите C:\\ShiftClub\\PostgreSQL вручную.";
            }
        }

        report("Удаляю файлы сервера…");
        if (ExeLivesIn(dir))
            return null;

        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception)
        {
            return "Службы сняты, но папка сервера ещё занята. Закройте это окно и удалите её вручную.";
        }

        return null;
    }

    public static void ScheduleDirectoryRemoval(string directory)
    {
        if (!ExeLivesIn(directory))
            return;
        Process.Start(new ProcessStartInfo("cmd.exe", ServerUninstallPlan.DelayedRemoveArguments(directory))
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }

    private static bool ExeLivesIn(string directory)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            return false;
        var root = Path.GetFullPath(directory).TrimEnd('\\') + "\\";
        return Path.GetFullPath(exe).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
