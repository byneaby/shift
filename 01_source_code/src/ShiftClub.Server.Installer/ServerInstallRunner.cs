using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using ShiftClub.Server.Setup;

namespace ShiftClub.Server.Installer;

internal sealed record InstallOutcome(bool Ok, string Message);

internal static class ServerInstallRunner
{
    public static async Task<InstallOutcome> RunAsync(ServerSetupAnswers answers, Action<string> report, CancellationToken cancellationToken)
    {
        var dir = answers.InstallDirectory.Trim().TrimEnd('\\');
        var secretsPath = Path.Combine(dir, "secrets.env");
        var secretsExist = File.Exists(secretsPath);
        var errors = ServerSetupValidator.Validate(answers, secretsExist);
        if (errors.Count > 0)
            return new InstallOutcome(false, string.Join(Environment.NewLine, errors));

        report("Создаю папку сервера…");
        Directory.CreateDirectory(dir);

        report("Копирую файлы сервера…");
        var copied = ExtractPayload(dir);
        WriteScript(dir, "script.Start-ShiftClubApi.ps1", "Start-ShiftClubApi.ps1");
        WriteScript(dir, "script.Watch-ShiftClubApi.ps1", "Watch-ShiftClubApi.ps1");
        if (!copied && !File.Exists(Path.Combine(dir, "ShiftClub.Server.dll")) && !File.Exists(Path.Combine(dir, "ShiftClub.Server.exe")))
        {
            return new InstallOutcome(false,
                "В этом установщике нет файлов сервера. Соберите его скриптом Build-ServerInstaller.ps1 — получится ShiftClub.Server.Setup.exe с сервером внутри.");
        }

        report(answers.CreateDatabase ? "Создаю базу…" : "Проверяю базу…");
        var dbError = answers.CreateDatabase
            ? await DatabaseSetup.EnsureDatabaseAsync(answers, cancellationToken)
            : await DatabaseSetup.TryConnectAsync(ServerSecrets.AppConnectionString(answers), cancellationToken);
        if (dbError is not null)
            return new InstallOutcome(false, dbError);

        report("Записываю настройки…");
        var secrets = ServerSecrets.Render(answers, ServerSecrets.CreateJwtKey(), ServerSecrets.CreateHiringPin());
        await File.WriteAllTextAsync(secretsPath, secrets, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);

        var notes = new List<string>();
        if (answers.OpenFirewall)
        {
            report("Открываю порт в брандмауэре…");
            if (!Run("netsh", WindowsInstallPlan.FirewallArguments(answers.ListenPort)))
                notes.Add($"Порт {answers.ListenPort} не удалось открыть автоматически. Разрешите его в брандмауэре Windows для частной сети.");
        }

        if (answers.StartWithWindows)
        {
            report("Включаю автозапуск…");
            var scriptPath = Path.Combine(dir, "register-tasks.ps1");
            await File.WriteAllTextAsync(scriptPath, WindowsInstallPlan.RegisterTasksScript(dir), new UTF8Encoding(true), cancellationToken);
            if (!Run("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\""))
                notes.Add("Автозапуск не включился. Сервер можно запустить файлом Start-ShiftClubApi.ps1 из папки установки.");
            else
                TryDelete(scriptPath);
        }

        var done = new StringBuilder();
        done.AppendLine("Сервер установлен.");
        done.AppendLine(WindowsInstallPlan.PanelHint(answers.ListenPort));
        done.AppendLine($"Логин владельца: {answers.OwnerLogin.Trim()}");
        done.Append(answers.LicenseLater
            ? "Ключ лицензии не записан. При открытии панели сервер попросит его."
            : "Ключ лицензии записан. Если центр лицензий доступен, зал включится сам.");
        if (notes.Count > 0)
        {
            done.AppendLine();
            done.AppendLine();
            done.Append(string.Join(Environment.NewLine, notes));
        }
        return new InstallOutcome(true, done.ToString());
    }

    private static bool ExtractPayload(string dir)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("payload.server.zip");
        if (stream is null)
            return false;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;
            if (WindowsInstallPlan.ShouldSkipExtract(entry.FullName))
                continue;

            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var dest = Path.GetFullPath(Path.Combine(dir, relative));
            var root = Path.GetFullPath(dir + Path.DirectorySeparatorChar);
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }
        return true;
    }

    private static void WriteScript(string dir, string resourceName, string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"В установщике нет файла {fileName}.");
        var dest = Path.Combine(dir, fileName);
        using var output = File.Create(dest);
        stream.CopyTo(output);
    }

    private static bool Run(string file, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
                return false;
            process.WaitForExit(120_000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* the task is already registered */ }
    }
}
