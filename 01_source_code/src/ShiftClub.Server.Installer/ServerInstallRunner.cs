using System.Diagnostics;
using System.IO.Compression;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Principal;
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

        var notes = new List<string>();
        if (answers.InstallBundledPostgres)
        {
            var postgresError = await InstallPostgresAsync(answers, report, notes, cancellationToken);
            if (postgresError is not null)
                return new InstallOutcome(false, postgresError);
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

        if (answers.OpenFirewall)
        {
            report("Открываю порт в брандмауэре…");
            if (RunCode("netsh", WindowsInstallPlan.FirewallArguments(answers.ListenPort), 120_000) != 0)
                notes.Add($"Порт {answers.ListenPort} не удалось открыть автоматически. Разрешите его в брандмауэре Windows для частной сети.");
        }

        if (answers.StartWithWindows)
        {
            report("Включаю автозапуск…");
            var scriptPath = Path.Combine(dir, "register-tasks.ps1");
            await File.WriteAllTextAsync(scriptPath, WindowsInstallPlan.RegisterTasksScript(dir), new UTF8Encoding(true), cancellationToken);
            if (RunCode("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"", 120_000) != 0)
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
        if (answers.InstallBundledPostgres)
        {
            done.AppendLine();
            done.Append(PostgresBundle.SuperuserSavedMessage);
        }
        if (notes.Count > 0)
        {
            done.AppendLine();
            done.AppendLine();
            done.Append(string.Join(Environment.NewLine, notes));
        }
        return new InstallOutcome(true, done.ToString());
    }

    private static async Task<string?> InstallPostgresAsync(
        ServerSetupAnswers answers,
        Action<string> report,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        report("Ставлю PostgreSQL 16…");
        await using var zipStream = OpenPayload(PostgresBundle.ZipResourceName);
        await using var redistStream = OpenPayload(PostgresBundle.VcRedistResourceName);
        if (zipStream is null || redistStream is null)
            return PostgresBundle.RebuildMessage;

        var dataDir = PostgresBundle.DataDirectory;
        var initialized = PostgresBundle.IsInitialized(dataDir);
        var portOpen = await IsPortOpenAsync("127.0.0.1", PostgresBundle.Port, cancellationToken);
        var decision = PostgresBundle.Decide(
            payloadPresent: true,
            portOpen,
            initialized,
            superuserFileExists: File.Exists(PostgresBundle.SuperuserFile));
        switch (decision)
        {
            case PostgresBundle.Decision.PortTaken:
                return PostgresBundle.PortTakenMessage;
            case PostgresBundle.Decision.SuperuserUnknown:
                return PostgresBundle.SuperuserUnknownMessage;
            case PostgresBundle.Decision.PayloadMissing:
                return PostgresBundle.RebuildMessage;
        }

        var (serviceExit, serviceConfig) = RunCapture("sc.exe", PostgresBundle.QueryServiceArguments(), 30_000);
        if (serviceExit == 0 && !PostgresBundle.ServicePointsAt(serviceConfig, dataDir))
            return PostgresBundle.ForeignServiceMessage;

        if (decision == PostgresBundle.Decision.Reuse && portOpen)
        {
            report("Останавливаю PostgreSQL перед обновлением файлов…");
            var stopped = File.Exists(PostgresBundle.PgCtlPath)
                ? RunCode(PostgresBundle.PgCtlPath, PostgresBundle.StopArguments(dataDir), 90_000, PostgresBundle.BinDirectory)
                : -1;
            if (stopped != 0)
                RunCode("sc.exe", "stop " + PostgresBundle.ServiceName, 60_000);
            portOpen = false;
        }

        Directory.CreateDirectory(PostgresBundle.InstallRoot);
        var zipPath = Path.Combine(PostgresBundle.InstallRoot, "_binaries.zip");
        try
        {
            report("Копирую файлы PostgreSQL…");
            await WriteStreamAsync(zipStream, zipPath, cancellationToken);
            using var zip = ZipFile.OpenRead(zipPath);
            if (PostgresBundle.ExtractBinaries(zip, PostgresBundle.InstallRoot) == 0)
                return PostgresBundle.InitFailedMessage;
        }
        finally
        {
            TryDelete(zipPath);
        }

        report("Ставлю Visual C++…");
        var redistPath = Path.Combine(Path.GetTempPath(), "ShiftClub-vc_redist.x64.exe");
        await WriteStreamAsync(redistStream, redistPath, cancellationToken);
        var vc = RunCode(redistPath, PostgresBundle.VcRedistArguments, 300_000);
        TryDelete(redistPath);
        if (!PostgresBundle.IsVcRedistSuccess(vc))
            return PostgresBundle.VcFailedMessage;
        if (PostgresBundle.NeedsReboot(vc))
            notes.Add(PostgresBundle.RebootMessage);

        if (!File.Exists(PostgresBundle.InitDbPath) || !File.Exists(PostgresBundle.PgCtlPath))
            return PostgresBundle.InitFailedMessage;

        string superuserPassword;
        if (decision == PostgresBundle.Decision.Initialize)
        {
            if (Directory.Exists(dataDir))
                Directory.Delete(dataDir, recursive: true);

            superuserPassword = PostgresBundle.CreatePassword();
            while (superuserPassword == answers.OwnerPassword)
                superuserPassword = PostgresBundle.CreatePassword();

            await File.WriteAllTextAsync(
                PostgresBundle.SuperuserFile,
                PostgresBundle.FormatSuperuserFile(superuserPassword),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

            var pwFile = Path.Combine(PostgresBundle.InstallRoot, "_pwfile.tmp");
            try
            {
                await File.WriteAllBytesAsync(pwFile, Encoding.ASCII.GetBytes(superuserPassword + "\n"), cancellationToken);
                report("Создаю кластер PostgreSQL…");
                var code = RunCode(
                    PostgresBundle.InitDbPath,
                    PostgresBundle.InitDbArguments(dataDir, pwFile),
                    300_000,
                    PostgresBundle.BinDirectory);
                if (code != 0 || !PostgresBundle.IsInitialized(dataDir))
                {
                    TryDelete(PostgresBundle.SuperuserFile);
                    return PostgresBundle.InitFailedMessage;
                }
            }
            finally
            {
                TryDelete(pwFile);
            }

            var confPath = Path.Combine(dataDir, "postgresql.conf");
            var conf = await File.ReadAllTextAsync(confPath, cancellationToken);
            var updated = PostgresBundle.EnsureListenLocal(conf);
            if (!string.Equals(conf, updated, StringComparison.Ordinal))
                await File.WriteAllTextAsync(confPath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        }
        else
        {
            superuserPassword = PostgresBundle.ReadSuperuserPassword(
                await File.ReadAllTextAsync(PostgresBundle.SuperuserFile, cancellationToken)) ?? "";
            if (superuserPassword.Length < 8)
                return PostgresBundle.SuperuserUnknownMessage;
        }

        if (RunCode("icacls.exe", PostgresBundle.GrantServiceAccessArguments(PostgresBundle.InstallRoot, "RX"), 120_000) != 0
            || RunCode("icacls.exe", PostgresBundle.GrantServiceAccessArguments(dataDir, "F"), 120_000) != 0)
            return PostgresBundle.DataAclMessage;
        RunCode("icacls.exe", PostgresBundle.LockdownFileArguments(PostgresBundle.SuperuserFile), 30_000);

        if (serviceExit != 0)
        {
            var registered = RunCode(
                PostgresBundle.PgCtlPath,
                PostgresBundle.RegisterServiceArguments(dataDir, ServiceAccount()),
                60_000,
                PostgresBundle.BinDirectory);
            if (registered != 0)
                return PostgresBundle.StartFailedMessage;
        }

        if (!portOpen)
        {
            report("Запускаю PostgreSQL…");
            var started = RunCode(PostgresBundle.PgCtlPath, PostgresBundle.StartArguments(dataDir), 120_000, PostgresBundle.BinDirectory);
            if (started != 0 && RunCode("sc.exe", "start " + PostgresBundle.ServiceName, 60_000) != 0)
                return PostgresBundle.StartFailedMessage;
        }

        if (!await WaitForPortAsync("127.0.0.1", PostgresBundle.Port, TimeSpan.FromSeconds(90), cancellationToken))
            return PostgresBundle.StartFailedMessage;

        PostgresBundle.AssignRuntimeSecrets(answers, superuserPassword);
        return null;
    }

    private static string ServiceAccount()
    {
        try
        {
            return new SecurityIdentifier("S-1-5-20").Translate(typeof(NTAccount)).Value;
        }
        catch
        {
            return @"NT AUTHORITY\NETWORK SERVICE";
        }
    }

    private static async Task<bool> WaitForPortAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsPortOpenAsync(host, port, cancellationToken))
                return true;
            await Task.Delay(500, cancellationToken);
        }
        return false;
    }

    private static async Task<bool> IsPortOpenAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(host, port, timeout.Token);
            return true;
        }
        catch
        {
            return false;
        }
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

    private static Stream? OpenPayload(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(name);

    private static async Task WriteStreamAsync(Stream source, string path, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var output = File.Create(path);
        await source.CopyToAsync(output, cancellationToken);
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

    private static int RunCode(string file, string arguments, int timeoutMs, string? workingDirectory = null)
    {
        var (code, _) = RunCapture(file, arguments, timeoutMs, workingDirectory);
        return code;
    }

    private static (int ExitCode, string Output) RunCapture(string file, string arguments, int timeoutMs, string? workingDirectory = null)
    {
        try
        {
            var info = new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                info.WorkingDirectory = workingDirectory;
                info.Environment["PATH"] = workingDirectory + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? "");
            }

            using var process = Process.Start(info);
            if (process is null)
                return (-1, "");

            var output = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return (-1, "");
            }

            process.WaitForExit();
            return (process.ExitCode, output.ToString());
        }
        catch
        {
            return (-1, "");
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* retry on the next run */ }
    }
}
