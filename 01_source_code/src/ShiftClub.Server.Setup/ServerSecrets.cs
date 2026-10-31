using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace ShiftClub.Server.Setup;

public static class ServerSecrets
{
    public static string CreateJwtKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    public static string CreateHiringPin()
    {
        var n = RandomNumberGenerator.GetInt32(1000, 10000);
        return n.ToString();
    }

    public static string ListenUrl(int port) => $"http://0.0.0.0:{port}";

    public static string AppConnectionString(ServerSetupAnswers answers)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = answers.DatabaseHost.Trim(),
            Port = answers.DatabasePort,
            Database = answers.DatabaseName.Trim(),
            Username = answers.DatabaseUser.Trim(),
            Password = answers.DatabasePassword
        };
        return builder.ConnectionString;
    }

    public static string AdminConnectionString(ServerSetupAnswers answers)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = answers.DatabaseHost.Trim(),
            Port = answers.DatabasePort,
            Database = "postgres",
            Username = answers.PostgresAdminUser.Trim(),
            Password = answers.PostgresAdminPassword,
            Timeout = 8,
            CommandTimeout = 15
        };
        return builder.ConnectionString;
    }

    public static string Render(ServerSetupAnswers answers, string jwtKey, string hiringPin)
    {
        if (jwtKey.Length < 32)
            throw new ArgumentException("Ключ подписи слишком короткий.", nameof(jwtKey));
        if (hiringPin.Length != 4 || hiringPin.Any(c => !char.IsDigit(c)))
            throw new ArgumentException("PIN найма должен быть из 4 цифр.", nameof(hiringPin));

        var key = answers.LicenseLater ? "" : ShiftClub.Shared.Licensing.LicenseKeyFormat.Normalize(answers.LicenseKey);
        var lines = new[]
        {
            "# Настройки сервера SHIFT Club. Файл создан мастером установки.",
            "# Пароль администратора PostgreSQL сюда не записывается.",
            "",
            "ConnectionStrings__Default=" + AppConnectionString(answers),
            "Database__Host=" + answers.DatabaseHost.Trim(),
            "Database__Port=" + answers.DatabasePort,
            "Jwt__SigningKey=" + jwtKey,
            "Jwt__ExpirationMinutes=720",
            "Seed__OwnerLogin=" + answers.OwnerLogin.Trim(),
            "Seed__OwnerPassword=" + answers.OwnerPassword,
            "EnableSwagger=false",
            "Hiring__ManagerPin=" + hiringPin,
            "Hiring__SessionHours=12",
            "",
            "License__Enforce=true",
            "License__ServerUrl=" + answers.LicenseServerUrl.Trim().TrimEnd('/'),
            "License__Key=" + key,
            "Kestrel__Endpoints__Http__Url=" + ListenUrl(answers.ListenPort),
            ""
        };

        var text = string.Join("\r\n", lines);
        if (answers.PostgresAdminPassword.Length > 0
            && answers.PostgresAdminPassword != answers.DatabasePassword
            && answers.PostgresAdminPassword != answers.OwnerPassword
            && lines.Any(line => line.Contains('=') && line[(line.IndexOf('=') + 1)..] == answers.PostgresAdminPassword))
            throw new InvalidOperationException("Пароль администратора PostgreSQL попал в файл настроек.");

        return text;
    }
}
