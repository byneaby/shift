using System.Globalization;

namespace ShiftClub.Application.Import;

public sealed record ParsedCustomerRow(
    int LineNumber,
    string FirstName,
    string LastName,
    string Phone,
    string? Email,
    decimal Balance,
    decimal BonusBalance,
    DateOnly? BirthDate,
    string? Notes,
    string? Problem)
{
    public bool IsValid => Problem is null;
}

public sealed record CsvParseResult(
    IReadOnlyList<ParsedCustomerRow> Rows,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> RecognizedColumns);

/// <summary>
/// Разбор выгрузки клиентов из старой системы.
/// Клубы выгружают что угодно: запятая или точка с запятой, «Телефон» или «phone»,
/// «1 500,50» или «1500.5». Угадываем формат, а всё, что не разобралось, показываем
/// в предпросмотре отдельной строкой — вместо того чтобы молча потерять клиента.
/// </summary>
public static class CustomerCsvParser
{
    private const int MaxRows = 20_000;

    private static readonly Dictionary<string, string> ColumnAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["phone"] = "phone",
        ["телефон"] = "phone",
        ["тел"] = "phone",
        ["номер"] = "phone",
        ["mobile"] = "phone",

        ["firstname"] = "first_name",
        ["first_name"] = "first_name",
        ["name"] = "first_name",
        ["имя"] = "first_name",

        ["lastname"] = "last_name",
        ["last_name"] = "last_name",
        ["surname"] = "last_name",
        ["фамилия"] = "last_name",

        ["фио"] = "full_name",
        ["ф.и.о."] = "full_name",
        ["fullname"] = "full_name",
        ["full_name"] = "full_name",
        ["клиент"] = "full_name",

        ["email"] = "email",
        ["почта"] = "email",
        ["e-mail"] = "email",

        ["balance"] = "balance",
        ["баланс"] = "balance",
        ["деньги"] = "balance",
        ["счет"] = "balance",
        ["счёт"] = "balance",

        ["bonus"] = "bonus",
        ["бонус"] = "bonus",
        ["бонусы"] = "bonus",

        ["birthdate"] = "birth_date",
        ["birth_date"] = "birth_date",
        ["дата рождения"] = "birth_date",
        ["др"] = "birth_date",

        ["notes"] = "notes",
        ["note"] = "notes",
        ["примечание"] = "notes",
        ["комментарий"] = "notes",
        ["коммент"] = "notes"
    };

    public static CsvParseResult Parse(string? csv)
    {
        var warnings = new List<string>();
        var rows = new List<ParsedCustomerRow>();

        if (string.IsNullOrWhiteSpace(csv))
            return new CsvParseResult(rows, ["Файл пустой."], []);

        // BOM от Excel иначе приклеивается к первому заголовку и ломает распознавание.
        csv = csv.TrimStart('\uFEFF');

        var lines = SplitLines(csv);
        if (lines.Count == 0)
            return new CsvParseResult(rows, ["Файл пустой."], []);

        var separator = DetectSeparator(lines[0]);
        var header = SplitCsvLine(lines[0], separator);
        var map = MapColumns(header);

        if (!map.ContainsKey("phone"))
        {
            return new CsvParseResult(
                rows,
                ["Не найден столбец с телефоном. Нужен заголовок «Телефон» или «phone»."],
                [.. map.Keys]);
        }

        if (!map.ContainsKey("first_name") && !map.ContainsKey("full_name"))
            warnings.Add("Нет столбца с именем — клиенты будут заведены как «Гость».");

        if (lines.Count - 1 > MaxRows)
            warnings.Add($"В файле больше {MaxRows} строк. Обработаем первые {MaxRows}.");

        var seenPhones = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 1; i < lines.Count && rows.Count < MaxRows; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var cells = SplitCsvLine(line, separator);
            var phoneRaw = Get(cells, map, "phone");
            var phone = NormalizePhone(phoneRaw);

            var firstName = Get(cells, map, "first_name")?.Trim() ?? "";
            var lastName = Get(cells, map, "last_name")?.Trim() ?? "";
            var fullName = Get(cells, map, "full_name")?.Trim() ?? "";

            // Порядок слов зависит от того, как назван столбец: в «ФИО» первым идёт
            // фамилия, в «Имя» — имя. Угадывать по самим словам нельзя.
            if (lastName.Length == 0 && fullName.Length > 0)
                (lastName, firstName) = SplitName(fullName, surnameFirst: true);
            else if (lastName.Length == 0 && firstName.Contains(' '))
                (lastName, firstName) = SplitName(firstName, surnameFirst: false);

            if (firstName.Length == 0)
                firstName = "Гость";

            string? problem = null;
            if (phone.Length < 10)
            {
                problem = string.IsNullOrWhiteSpace(phoneRaw)
                    ? "Нет телефона"
                    : $"Телефон «{Shorten(phoneRaw)}» не похож на номер";
            }
            else if (seenPhones.TryGetValue(phone, out var firstLine))
            {
                problem = $"Такой же телефон уже был в строке {firstLine}";
            }
            else
            {
                seenPhones[phone] = lineNumber;
            }

            var (balance, balanceProblem) = ParseMoney(Get(cells, map, "balance"));
            var (bonus, bonusProblem) = ParseMoney(Get(cells, map, "bonus"));
            problem ??= balanceProblem is null ? null : $"Баланс: {balanceProblem}";
            problem ??= bonusProblem is null ? null : $"Бонусы: {bonusProblem}";

            if (problem is null && balance < 0)
                problem = "Отрицательный баланс — заведите такого клиента вручную";

            rows.Add(new ParsedCustomerRow(
                lineNumber,
                Truncate(firstName, 60),
                Truncate(lastName, 60),
                phone,
                CleanEmail(Get(cells, map, "email")),
                balance,
                Math.Max(0, bonus),
                ParseDate(Get(cells, map, "birth_date")),
                Truncate(Get(cells, map, "notes")?.Trim(), 400),
                problem));
        }

        if (rows.Count == 0)
            warnings.Add("В файле нет строк с данными — только заголовок.");

        return new CsvParseResult(rows, warnings, [.. map.Keys]);
    }

    private static (string LastName, string FirstName) SplitName(string value, bool surnameFirst)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return ("", parts.FirstOrDefault() ?? "");

        return surnameFirst
            ? (parts[0], string.Join(' ', parts.Skip(1)))
            : (string.Join(' ', parts.Skip(1)), parts[0]);
    }

    private static List<string> SplitLines(string csv) =>
        csv.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Where(l => l.Trim().Length > 0)
            .ToList();

    private static char DetectSeparator(string header)
    {
        var semicolons = header.Count(c => c == ';');
        var commas = header.Count(c => c == ',');
        var tabs = header.Count(c => c == '\t');

        if (tabs > semicolons && tabs > commas)
            return '\t';
        return semicolons >= commas ? ';' : ',';
    }

    private static Dictionary<string, int> MapColumns(List<string> header)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < header.Count; i++)
        {
            var key = header[i].Trim().Trim('"').ToLowerInvariant();
            if (ColumnAliases.TryGetValue(key, out var canonical) && !map.ContainsKey(canonical))
                map[canonical] = i;
        }

        return map;
    }

    private static string? Get(List<string> cells, Dictionary<string, int> map, string column)
    {
        if (!map.TryGetValue(column, out var index) || index >= cells.Count)
            return null;
        var value = cells[index];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Разбор строки с учётом кавычек: внутри «"Иванов, Иван"» запятая не разделитель.</summary>
    internal static List<string> SplitCsvLine(string line, char separator)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(ch);
                }

                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == separator)
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        cells.Add(current.ToString().Trim());
        return cells;
    }

    private static string NormalizePhone(string? raw)
    {
        var digits = new string((raw ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits[0] == '8')
            digits = "7" + digits[1..];
        else if (digits.Length == 10)
            digits = "7" + digits;
        return digits;
    }

    private static (decimal Value, string? Problem) ParseMoney(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (0m, null);

        // «1 500,50 ₸» → «1500.50»
        var cleaned = new string(raw.Where(c => char.IsDigit(c) || c is '.' or ',' or '-').ToArray())
            .Replace(',', '.');

        if (cleaned.Length == 0)
            return (0m, null);

        // Если точек больше одной, первые — разделители тысяч.
        var lastDot = cleaned.LastIndexOf('.');
        if (cleaned.Count(c => c == '.') > 1 && lastDot > 0)
            cleaned = cleaned[..lastDot].Replace(".", "") + cleaned[lastDot..];

        if (!decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            return (0m, $"«{Shorten(raw)}» не число");

        return (Math.Round(value, 2), null);
    }

    private static DateOnly? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        string[] formats = ["dd.MM.yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "d.M.yyyy", "yyyy.MM.dd"];
        foreach (var format in formats)
        {
            if (DateOnly.TryParseExact(raw.Trim(), format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d;
        }

        return null;
    }

    private static string? CleanEmail(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
            return null;
        return value.Contains('@') && value.Length <= 200 ? value : null;
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];

    private static string Shorten(string value) =>
        value.Length <= 24 ? value.Trim() : value.Trim()[..24] + "…";
}
