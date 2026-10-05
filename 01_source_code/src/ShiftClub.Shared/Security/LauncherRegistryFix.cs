using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace ShiftClub.Shared.Security;

/// <summary>
/// Cafe / diskless fix: Steam ("registry path is currently not writable") and sibling launchers
/// need Users write access under HKLM\SOFTWARE\...\Valve (and Epic/Riot/Blizzard trees).
/// Safe to call repeatedly from SYSTEM service or elevated GameFix.
/// </summary>
public static class LauncherRegistryFix
{
    private static readonly string[] WritableTrees =
    [
        @"SOFTWARE\WOW6432Node\Valve",
        @"SOFTWARE\WOW6432Node\Valve\Steam",
        @"SOFTWARE\Valve",
        @"SOFTWARE\Valve\Steam",
        @"SOFTWARE\WOW6432Node\Epic Games",
        @"SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher",
        @"SOFTWARE\Epic Games",
        @"SOFTWARE\Epic Games\EpicGamesLauncher",
        @"SOFTWARE\WOW6432Node\Riot Games",
        @"SOFTWARE\Riot Games",
        @"SOFTWARE\WOW6432Node\Blizzard Entertainment",
        @"SOFTWARE\WOW6432Node\Blizzard Entertainment\Battle.net",
        @"SOFTWARE\Blizzard Entertainment",
        @"SOFTWARE\WOW6432Node\Electronic Arts",
        @"SOFTWARE\Electronic Arts",
        @"SOFTWARE\WOW6432Node\Ubisoft",
        @"SOFTWARE\WOW6432Node\Rockstar Games",
        @"SOFTWARE\Classes\steam",
        @"SOFTWARE\Classes\com.epicgames.launcher",
        @"SOFTWARE\Classes\riotclient",
        @"SOFTWARE\Classes\battlenet",
    ];

    public static void Apply(string steamPath = @"F:\Steam")
    {
        foreach (var tree in WritableTrees)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(tree, true);
                if (key is null)
                    continue;
                GrantUsersFullControl(key);
            }
            catch
            {
                /* ignore per-key */
            }
        }

        try
        {
            var steam = steamPath.TrimEnd('\\');
            var steamExe = Path.Combine(steam, "steam.exe");
            if (File.Exists(steamExe))
            {
                SetSz(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", steam);
                SetSz(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath", steam);

                var slash = steam.Replace('\\', '/').ToLowerInvariant();
                SetSz(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath", slash);
                SetSz(Registry.CurrentUser, @"Software\Valve\Steam", "SteamExe", slash + "/steam.exe");
            }
        }
        catch
        {
            /* ignore */
        }

        try
        {
            if (Directory.Exists(@"F:\Battle.net"))
            {
                SetSz(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Blizzard Entertainment\Battle.net", "InstallPath", @"F:\Battle.net");
                SetSz(Registry.LocalMachine, @"SOFTWARE\Blizzard Entertainment\Battle.net", "InstallPath", @"F:\Battle.net");
            }
        }
        catch { /* ignore */ }

        try
        {
            if (Directory.Exists(@"F:\Riot Games\Riot Client"))
            {
                SetSz(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Riot Games\Riot Client", "Install Folder", @"F:\Riot Games\Riot Client");
                SetSz(Registry.LocalMachine, @"SOFTWARE\Riot Games\Riot Client", "Install Folder", @"F:\Riot Games\Riot Client");
            }
        }
        catch { /* ignore */ }
    }

    private static void GrantUsersFullControl(RegistryKey key)
    {
        var acl = key.GetAccessControl();
        var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        foreach (var account in new[]
                 {
                     new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                     new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                 })
        {
            var rule = new RegistryAccessRule(
                account,
                RegistryRights.FullControl,
                inherit,
                PropagationFlags.None,
                AccessControlType.Allow);
            acl.SetAccessRule(rule);
        }

        key.SetAccessControl(acl);
    }

    private static void SetSz(RegistryKey root, string subPath, string name, string value)
    {
        using var key = root.CreateSubKey(subPath, true);
        key?.SetValue(name, value, RegistryValueKind.String);
    }
}
