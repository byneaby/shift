using System.Text.RegularExpressions;
using ShiftClub.Shared.Licensing;

namespace ShiftClub.Server.Setup;

public static class ServerSetupValidator
{
    private static readonly Regex SqlName = new("^[A-Za-z_][A-Za-z0-9_]{0,62}$", RegexOptions.CultureInvariant);
    private static readonly Regex OwnerLogin = new("^[A-Za-z0-9._-]{3,32}$", RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Validate(ServerSetupAnswers answers, bool secretsAlreadyExist)
    {
        var errors = new List<string>();
        errors.AddRange(ValidateFolder(answers, secretsAlreadyExist));
        errors.AddRange(ValidateDatabase(answers));
        errors.AddRange(ValidateOwner(answers));
        errors.AddRange(ValidateLicense(answers));
        return errors;
    }

    public static IReadOnlyList<string> ValidateFolder(ServerSetupAnswers answers, bool secretsAlreadyExist)
    {
        var errors = new List<string>();
        var dir = (answers.InstallDirectory ?? "").Trim();
        if (dir.Length == 0)
            errors.Add("Укажите папку, куда ставить сервер.");
        else if (!IsSafeInstallDirectory(dir))
            errors.Add("Папка должна быть вида C:\\ShiftClub\\Server и не должна быть корнем диска или папкой Windows.");

        if (answers.ListenPort is < 1024 or > 65535)
            errors.Add("Порт панели — число от 1024 до 65535. Обычно оставляют 5080.");
        else if (answers.ListenPort == answers.DatabasePort)
            errors.Add("Порт панели и порт базы не могут совпадать.");

        if (secretsAlreadyExist && !answers.ReplaceExistingSecrets)
            errors.Add("В этой папке сервер уже настроен. Отметьте замену файла настроек или выберите другую папку. База и папка data не удаляются.");

        return errors;
    }

    public static IReadOnlyList<string> ValidateDatabase(ServerSetupAnswers answers)
    {
        var errors = new List<string>();
        var host = (answers.DatabaseHost ?? "").Trim();
        if (host.Length == 0 || host.Any(char.IsWhiteSpace))
            errors.Add("Укажите адрес PostgreSQL, обычно 127.0.0.1.");

        if (answers.DatabasePort is < 1 or > 65535)
            errors.Add("Порт PostgreSQL — число от 1 до 65535. Обычно 5432.");

        if (!SqlName.IsMatch(answers.DatabaseName ?? ""))
            errors.Add("Имя базы — латиница, цифры и подчёркивание. Например shiftclub.");

        if (!SqlName.IsMatch(answers.DatabaseUser ?? ""))
            errors.Add("Имя пользователя базы — латиница, цифры и подчёркивание. Например shiftclub.");

        if (answers.CreateDatabase && !SqlName.IsMatch(answers.PostgresAdminUser ?? ""))
            errors.Add("Имя администратора PostgreSQL — обычно postgres.");

        errors.AddRange(PasswordErrors(answers.DatabasePassword, "Пароль базы"));
        if (answers.CreateDatabase)
            errors.AddRange(PasswordErrors(answers.PostgresAdminPassword, "Пароль администратора PostgreSQL"));

        return errors;
    }

    public static IReadOnlyList<string> ValidateOwner(ServerSetupAnswers answers)
    {
        var errors = new List<string>();
        if (!OwnerLogin.IsMatch(answers.OwnerLogin ?? ""))
            errors.Add("Логин владельца: 3–32 символа, латиница, цифры, точка, минус или подчёркивание.");

        errors.AddRange(PasswordErrors(answers.OwnerPassword, "Пароль владельца"));
        if ((answers.OwnerPassword ?? "") != (answers.OwnerPasswordConfirm ?? ""))
            errors.Add("Пароль владельца и повтор не совпадают.");

        return errors;
    }

    public static IReadOnlyList<string> ValidateLicense(ServerSetupAnswers answers)
    {
        var errors = new List<string>();
        if (!answers.LicenseLater && !LicenseKeyFormat.IsValid(answers.LicenseKey))
            errors.Add("Ключ лицензии должен быть вида SHIFT-XXXX-XXXX-XXXX. Либо отметьте, что вставите его после запуска.");

        var url = (answers.LicenseServerUrl ?? "").Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(uri.UserInfo))
            errors.Add("Адрес центра лицензий должен начинаться с https://. Обычно https://licence.shift-club.kz.");

        return errors;
    }

    public static bool IsSafeInstallDirectory(string path)
    {
        var dir = (path ?? "").Trim().TrimEnd('\\', '/');
        if (dir.Length == 0 || dir.Contains("..", StringComparison.Ordinal))
            return false;
        if (dir.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
            return false;

        var match = Regex.Match(dir, @"^[A-Za-z]:\\(.+)$", RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;

        var first = match.Groups[1].Value.Split('\\')[0];
        if (first.Length == 0)
            return false;

        var blocked = new[] { "windows", "program files", "program files (x86)", "users", "programdata" };
        return !blocked.Contains(first, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> PasswordErrors(string? password, string label)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
        {
            yield return $"{label}: минимум 8 символов.";
            yield break;
        }

        if (password.Any(c => c is '\r' or '\n' or '\0'))
            yield return $"{label} не должен содержать перенос строки.";
    }
}
