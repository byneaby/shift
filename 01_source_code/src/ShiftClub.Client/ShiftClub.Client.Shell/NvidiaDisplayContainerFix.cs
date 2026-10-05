using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ShiftClub.Client.Shell;

/// <summary>
/// Оживление NVIDIA Display Container (CCBoot / InfGPU).
/// Служба часто «Выполняется», но процесс зомби — CPL не открывается,
/// пока не убить NVDisplay.Container.exe и не поднять службу снова.
/// Права на kill — у ShiftClubClient (SYSTEM); Shell только шлёт request.
/// </summary>
internal static class NvidiaDisplayContainerFix
{
    public static readonly string[] ProcessNames =
    [
        "NVDisplay.Container",
        "NVDisplayContainer"
    ];

    public static readonly string[] ServiceNames =
    [
        "NVDisplay.ContainerLocalSystem",
        "NVDisplayContainerLocalSystem",
        "NvContainerLocalSystem"
    ];

    /// <summary>Найти NVDisplay.Container.exe в DriverStore (хеш папки меняется с драйвером).</summary>
    public static string? FindContainerExe()
    {
        try
        {
            var roots = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "DriverStore", "FileRepository"),
                @"C:\Windows\System32\DriverStore\FileRepository",
                @"C:\CCBoot\InfGPU"
            };

            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    var hit = Directory.EnumerateFiles(root, "NVDisplay.Container.exe", SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(hit))
                        return hit;
                }
                catch { /* access */ }
            }
        }
        catch { /* ignore */ }

        return null;
    }

    public static string? FindRecoveryBat()
    {
        try
        {
            var exe = FindContainerExe();
            if (exe is not null)
            {
                var bat = Path.Combine(Path.GetDirectoryName(exe)!, "NvContainerRecovery.bat");
                if (File.Exists(bat)) return bat;
            }

            const string inf = @"C:\CCBoot\InfGPU";
            if (Directory.Exists(inf))
            {
                return Directory.EnumerateFiles(inf, "NvContainerRecovery.bat", SearchOption.AllDirectories)
                    .FirstOrDefault();
            }
        }
        catch { /* ignore */ }

        return null;
    }
}
