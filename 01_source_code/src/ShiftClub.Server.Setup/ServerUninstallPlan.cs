namespace ShiftClub.Server.Setup;

public static class ServerUninstallPlan
{
    public const string RegistryKey = @"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\ShiftClubServer";
    public const string UninstallExeName = "Uninstall.exe";

    public static string FirewallDeleteArguments(string ruleName)
    {
        var name = (ruleName ?? "").Replace("\"", "", StringComparison.Ordinal).Trim();
        if (name.Length == 0)
            throw new ArgumentException("Пустое имя правила.", nameof(ruleName));
        return $"advfirewall firewall delete rule name=\"{name}\"";
    }

    public static IReadOnlyList<string> FirewallRulesToDelete(ServerInstallMarker marker)
    {
        var names = new List<string>();
        if (!string.IsNullOrWhiteSpace(marker.FirewallRuleName))
            names.Add(marker.FirewallRuleName.Trim());
        if (marker.ListenPort is > 0 and < 65536)
            names.Add(WindowsInstallPlan.LegacyFirewallRuleName(marker.ListenPort));
        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => name.IndexOfAny(new[] { '"', '\r', '\n' }) < 0)
            .ToArray();
    }

    public static string AddRegistryArguments(string valueName, string data)
    {
        if (valueName.Any(c => !char.IsLetterOrDigit(c)))
            throw new ArgumentException("Недопустимое имя значения.", nameof(valueName));
        var escaped = data.Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"add \"{RegistryKey}\" /v {valueName} /t REG_SZ /d \"{escaped}\" /f";
    }

    public static string DeleteRegistryArguments() => $"delete \"{RegistryKey}\" /f";

    public static string UninstallString(string exePath) => $"\"{exePath}\" /uninstall";

    public static string DelayedRemoveArguments(string directory)
    {
        if (!ServerSetupValidator.IsSafeInstallDirectory(directory))
            throw new ArgumentException("Эту папку удалять нельзя.", nameof(directory));
        return $"/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{directory.Trim().TrimEnd('\\')}\"";
    }

    public static bool CanRemoveBundledPostgres(ServerInstallMarker marker, bool requested) =>
        requested && marker.BundledPostgres && ServerSetupValidator.IsSafeInstallDirectory(PostgresBundle.InstallRoot);
}
