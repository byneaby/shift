using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace ShiftClub.Server.Setup;

/// <summary>
/// PostgreSQL 16 binaries shipped inside the setup EXE.
/// The official EDB zip is downloaded by Build-ServerInstaller.ps1 and is not committed.
/// </summary>
public static class PostgresBundle
{
    public const string VersionLabel = "16.15-5";
    public const string BinariesUrl = "https://get.enterprisedb.com/postgresql/postgresql-16.15-5-windows-x64-binaries.zip";
    public const string BinariesSha256 = "43bb45f173a6f08cf1d29a97a6d8deb119e8e8093a24c00d2d1001a0ccaa8281";
    public const string VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";

    public const string InstallRoot = @"C:\ShiftClub\PostgreSQL";
    public const string ServiceName = "postgresql-x64-16";
    public const string Superuser = "postgres";
    public const int Port = 5432;
    public const string ZipResourceName = "payload.postgres.zip";
    public const string VcRedistResourceName = "payload.vc_redist.x64.exe";
    public const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public static string DataDirectory => InstallRoot + @"\data";
    public static string BinDirectory => InstallRoot + @"\bin";
    public static string SuperuserFile => InstallRoot + @"\superuser.txt";
    public static string InitDbPath => BinDirectory + @"\initdb.exe";
    public static string PgCtlPath => BinDirectory + @"\pg_ctl.exe";

    public static string RebuildMessage =>
        "В этом установщике нет PostgreSQL 16 или Visual C++. Соберите его скриптом Build-ServerInstaller.ps1 — они попадут внутрь одного файла.";

    public static string PortTakenMessage =>
        "Порт 5432 уже занят другим PostgreSQL, а базы SHIFT Club ещё нет. На шаге базы снимите «Поставить PostgreSQL 16 вместе с сервером» и укажите пароль уже установленного PostgreSQL.";

    public static string ForeignServiceMessage =>
        "На компьютере уже есть служба PostgreSQL 16. На шаге базы снимите «Поставить PostgreSQL 16 вместе с сервером» и укажите пароль этого PostgreSQL.";

    public static string SuperuserUnknownMessage =>
        $"Папка данных PostgreSQL уже есть, но пароль суперпользователя не найден в {SuperuserFile}. Снимите «Поставить PostgreSQL 16 вместе с сервером» и укажите пароль существующей базы.";

    public static string InitFailedMessage =>
        "Не удалось подготовить PostgreSQL 16. Запустите установщик от имени администратора.";

    public static string StartFailedMessage =>
        "PostgreSQL 16 скопирован, но служба не запустилась. Запустите установщик от имени администратора и проверьте, что порт 5432 свободен.";

    public static string VcFailedMessage =>
        "Не удалось поставить Visual C++. Он нужен PostgreSQL 16. Запустите установщик от имени администратора.";

    public static string DataAclMessage =>
        "Не удалось выдать службе PostgreSQL доступ к папке данных. Запустите установщик от имени администратора.";

    public static string SuperuserSavedMessage =>
        $"Пароль суперпользователя PostgreSQL записан в {SuperuserFile}.";

    public static string RebootMessage =>
        "Компонент Visual C++ попросил перезагрузить Windows. После перезагрузки сервер клуба поднимется сам.";

    public enum Decision
    {
        Initialize,
        Reuse,
        PortTaken,
        SuperuserUnknown,
        PayloadMissing
    }

    public static Decision Decide(bool payloadPresent, bool portOpen, bool dataInitialized, bool superuserFileExists)
    {
        if (!payloadPresent)
            return Decision.PayloadMissing;
        if (dataInitialized && !superuserFileExists)
            return Decision.SuperuserUnknown;
        if (!dataInitialized && portOpen)
            return Decision.PortTaken;
        if (dataInitialized)
            return Decision.Reuse;
        return Decision.Initialize;
    }

    public static bool IsInitialized(string dataDirectory) =>
        File.Exists(Path.Combine(dataDirectory, "PG_VERSION"));

    public static string CreatePassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
            chars[i] = PasswordAlphabet[bytes[i] % PasswordAlphabet.Length];
        return new string(chars);
    }

    public static void AssignRuntimeSecrets(ServerSetupAnswers answers, string superuserPassword)
    {
        if (string.IsNullOrEmpty(superuserPassword) || superuserPassword.Length < 8
            || superuserPassword.Any(c => c is '\r' or '\n' or '\0'))
            throw new ArgumentException("Пароль суперпользователя слишком короткий.", nameof(superuserPassword));

        answers.InstallBundledPostgres = true;
        answers.DatabaseHost = "127.0.0.1";
        answers.DatabasePort = Port;
        answers.CreateDatabase = true;
        answers.PostgresAdminUser = Superuser;
        answers.PostgresAdminPassword = superuserPassword;
        while (string.IsNullOrEmpty(answers.DatabasePassword) || answers.DatabasePassword == superuserPassword)
            answers.DatabasePassword = CreatePassword();
    }

    public static string? MapZipEntry(string entryName)
    {
        if (string.IsNullOrEmpty(entryName))
            return null;

        var name = entryName.Replace('\\', '/').TrimStart('/');
        if (name.Length == 0 || name.EndsWith("/", StringComparison.Ordinal))
            return null;
        if (name.StartsWith("pgsql/", StringComparison.OrdinalIgnoreCase))
            name = name["pgsql/".Length..];

        var allowed = name.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("share/", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("include/", StringComparison.OrdinalIgnoreCase);
        if (!allowed)
            return null;

        var parts = name.Split('/');
        if (parts.Any(part => part.Length == 0 || part == "." || part == ".."))
            return null;
        return name;
    }

    public static int ExtractBinaries(ZipArchive zip, string installRoot)
    {
        var root = Path.GetFullPath(installRoot);
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var count = 0;
        foreach (var entry in zip.Entries)
        {
            var relative = MapZipEntry(entry.FullName);
            if (relative is null)
                continue;

            var dest = root;
            foreach (var part in relative.Split('/'))
                dest = Path.Combine(dest, part);
            dest = Path.GetFullPath(dest);
            if (!dest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
            count++;
        }
        return count;
    }

    public static string InitDbArguments(string dataDirectory, string passwordFile) =>
        $"-D \"{dataDirectory}\" -U {Superuser} -E UTF8 --locale=C --pwfile=\"{passwordFile}\" --auth-local=scram-sha-256 --auth-host=scram-sha-256";

    public static string RegisterServiceArguments(string dataDirectory, string serviceAccount)
    {
        if (string.IsNullOrWhiteSpace(serviceAccount) || serviceAccount.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0)
            throw new ArgumentException("Недопустимое имя учётной записи службы.", nameof(serviceAccount));

        return $"register -N {ServiceName} -D \"{dataDirectory}\" -S auto -U \"{serviceAccount}\" -o \"-c listen_addresses=localhost -c port={Port}\"";
    }

    public static string StartArguments(string dataDirectory) =>
        $"start -D \"{dataDirectory}\" -w -t 60";

    public static string StopArguments(string dataDirectory) =>
        $"stop -D \"{dataDirectory}\" -m fast -t 60";

    public static string QueryServiceArguments() => $"qc {ServiceName}";

    public static string VcRedistArguments => "/install /quiet /norestart";

    public static bool IsVcRedistSuccess(int exitCode) => exitCode is 0 or 1638 or 3010 or 1641;

    public static bool NeedsReboot(int exitCode) => exitCode is 3010 or 1641;

    public static string GrantServiceAccessArguments(string directory, string rights)
    {
        if (rights is not ("RX" or "F"))
            throw new ArgumentException("Недопустимые права.", nameof(rights));
        return $"\"{directory}\" /grant *S-1-5-20:(OI)(CI){rights} /T /C";
    }

    public static string LockdownFileArguments(string path) =>
        $"\"{path}\" /inheritance:r /grant:r *S-1-5-32-544:F *S-1-5-18:F";

    public static bool ServicePointsAt(string serviceConfig, string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(serviceConfig) || string.IsNullOrWhiteSpace(dataDirectory))
            return false;
        return serviceConfig.Contains(dataDirectory, StringComparison.OrdinalIgnoreCase);
    }

    public static string EnsureListenLocal(string conf)
    {
        const string marker = "# SHIFT Club: listen on localhost only";
        if (conf.Contains(marker, StringComparison.Ordinal))
            return conf;

        var nl = conf.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var body = conf.TrimEnd('\r', '\n');
        return body + nl + marker + nl + "listen_addresses = 'localhost'" + nl + $"port = {Port}" + nl;
    }

    public static string FormatSuperuserFile(string password)
    {
        if (password.Length < 8 || password.Any(c => c is '\r' or '\n' or '\0' or '='))
            throw new ArgumentException("Пароль содержит недопустимый символ.", nameof(password));

        return "PostgreSQL superuser for SHIFT Club\r\nuser=postgres\r\npassword=" + password + "\r\n";
    }

    public static string BriefFailure(string? output, string secret)
    {
        if (string.IsNullOrWhiteSpace(output))
            return "";

        var text = string.IsNullOrEmpty(secret) ? output : output.Replace(secret, "***", StringComparison.Ordinal);
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var tail = string.Join(" ", lines.TakeLast(6)).Trim();
        if (tail.Length > 360)
            tail = tail[^360..];
        return tail.Length == 0 ? "" : "\n" + tail;
    }

    public static string? ReadSuperuserPassword(string fileText)
    {
        var lines = fileText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            const string prefix = "password=";
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            var value = line[prefix.Length..];
            if (value.Length >= 8 && value.IndexOfAny(new[] { '\0', '\r', '\n' }) < 0)
                return value;
        }
        return null;
    }
}
