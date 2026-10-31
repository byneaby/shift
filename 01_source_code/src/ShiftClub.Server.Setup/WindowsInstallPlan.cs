using System.Text;

namespace ShiftClub.Server.Setup;

public static class WindowsInstallPlan
{
    public static string FirewallArguments(int port) =>
        $"advfirewall firewall add rule name=\"SHIFT Club Server {port}\" dir=in action=allow protocol=TCP localport={port} profile=private,domain";

    public static string RegisterTasksScript(string installDirectory)
    {
        var dir = installDirectory.Trim().TrimEnd('\\');
        var startArg = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + dir + "\\Start-ShiftClubApi.ps1\"";
        var watchArg = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + dir + "\\Watch-ShiftClubApi.ps1\"";
        var qDir = PsQuote(dir);

        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine("$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -MultipleInstances IgnoreNew -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero)");
        sb.AppendLine("$settings.IdleSettings.StopOnIdleEnd = $false");
        sb.AppendLine("$settings.IdleSettings.RestartOnIdle = $false");
        sb.AppendLine("$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest");
        sb.AppendLine($"$startAction = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument {PsQuote(startArg)} -WorkingDirectory {qDir}");
        sb.AppendLine("$trigger = New-ScheduledTaskTrigger -AtStartup");
        sb.AppendLine("$trigger.Delay = 'PT45S'");
        sb.AppendLine("Register-ScheduledTask -TaskName 'ShiftClubApi' -Action $startAction -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null");
        sb.AppendLine($"$watchAction = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument {PsQuote(watchArg)} -WorkingDirectory {qDir}");
        sb.AppendLine("$watchTrigger = New-ScheduledTaskTrigger -Once -At (Get-Date).Date -RepetitionInterval (New-TimeSpan -Minutes 2) -RepetitionDuration (New-TimeSpan -Days 3650)");
        sb.AppendLine("$watchSettings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 2) -MultipleInstances IgnoreNew");
        sb.AppendLine("$watchSettings.IdleSettings.StopOnIdleEnd = $false");
        sb.AppendLine("Register-ScheduledTask -TaskName 'ShiftClubApiWatchdog' -Action $watchAction -Trigger $watchTrigger -Settings $watchSettings -Principal $principal -Force | Out-Null");
        sb.AppendLine("Start-ScheduledTask -TaskName 'ShiftClubApi'");
        return sb.ToString();
    }

    public static IReadOnlyList<(string Label, string Value)> Summary(ServerSetupAnswers answers)
    {
        var license = answers.LicenseLater
            ? "вставите после запуска, сервер попросит ключ"
            : ShiftClub.Shared.Licensing.LicenseKeyFormat.Normalize(answers.LicenseKey);
        var database = answers.InstallBundledPostgres
            ? $"PostgreSQL 16 в {PostgresBundle.InstallRoot}, слушает только этот компьютер"
            : answers.CreateDatabase
                ? $"создать {answers.DatabaseName.Trim()} на {answers.DatabaseHost.Trim()}:{answers.DatabasePort}"
                : $"уже есть {answers.DatabaseName.Trim()} на {answers.DatabaseHost.Trim()}:{answers.DatabasePort}";
        return new (string, string)[]
        {
            ("Папка", answers.InstallDirectory.Trim()),
            ("Панель", $"http://127.0.0.1:{answers.ListenPort}"),
            ("База", database),
            ("Владелец", answers.OwnerLogin.Trim()),
            ("Лицензия", license),
            ("Центр лицензий", answers.LicenseServerUrl.Trim()),
            ("Автозапуск", answers.StartWithWindows ? "да, вместе с Windows" : "нет, запуск вручную"),
            ("Сеть", answers.OpenFirewall ? $"открыть порт {answers.ListenPort} в локальной сети" : "правило брандмауэра не добавлять")
        };
    }

    public static string PanelHint(int port) =>
        $"На этом компьютере панель открывается по адресу http://127.0.0.1:{port}. С кассы и других ПК клуба откройте тот же адрес, подставив IP этого сервера.";

    public static bool ShouldSkipExtract(string entryName)
    {
        var name = entryName.Replace('\\', '/').TrimStart('/');
        if (name.Contains("..", StringComparison.Ordinal))
            return true;
        if (name.Equals("secrets.env", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.StartsWith("logs/", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static string PsQuote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
