using System.Runtime.Versioning;

namespace ShiftClub.Shared.Security;

/// <summary>
/// Diskless image / superclient (Senet/iCafe-style): when active, Keeper must not restart Shell
/// and Client.Service must not re-apply process DACL.
/// Flag: %ProgramData%\ShiftClub\Client\admin.mode
/// </summary>
[SupportedOSPlatform("windows")]
public static class ClientAdminMode
{
    public static string FlagDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ShiftClub",
        "Client");

    public static string FlagPath => Path.Combine(FlagDirectory, "admin.mode");

    /// <summary>Stale flags older than this are ignored (safety after forgotten admin session).</summary>
    public static TimeSpan MaxAge { get; set; } = TimeSpan.FromHours(12);

    public static void Enter()
    {
        Directory.CreateDirectory(FlagDirectory);
        File.WriteAllText(FlagPath, $"{DateTimeOffset.UtcNow:O}|admin");
    }

    /// <summary>Refresh flag timestamp while staff remains in admin mode.</summary>
    public static void Touch()
    {
        try
        {
            if (!File.Exists(FlagPath))
                Enter();
            else
                File.WriteAllText(FlagPath, $"{DateTimeOffset.UtcNow:O}|admin");
        }
        catch
        {
            /* ignore */
        }
    }

    public static void Exit()
    {
        try
        {
            if (File.Exists(FlagPath))
                File.Delete(FlagPath);
        }
        catch
        {
            /* ignore */
        }
    }

    public static bool IsActive()
    {
        try
        {
            if (!File.Exists(FlagPath))
                return false;

            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(FlagPath);
            if (age > MaxAge)
            {
                try { File.Delete(FlagPath); } catch { /* ignore */ }
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
